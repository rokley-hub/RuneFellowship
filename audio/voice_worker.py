"""A single reusable model process with a hard, reclaimable request deadline."""
import multiprocessing
import threading
import time

class VoiceCancelled(RuntimeError):
    pass

class BoundedVoiceWorker:
    def __init__(self, target, cooperative=False):
        self.target = target
        self.cooperative = cooperative
        self.cancel_event = None
        self.cancel_lock = threading.Lock()
        self.cancelled = {}
        self.active_id = ''
        self.lock = threading.Lock()
        self.process = self.pipe = None
        self.phase = 'idle'
        self.device = self.engine = ''
        self.started = self.last_use = 0.0
        self.leases = {}
        self.lease_lock = threading.Lock()
        self.release_when_idle = False

    def stop(self):
        if self.process is not None:
            if self.process.is_alive():
                self.process.terminate()
                self.process.join(2)
                if self.process.is_alive():
                    self.process.kill()
                    self.process.join(1)
            self.process.close()
        if self.pipe is not None:
            self.pipe.close()
        self.process = self.pipe = None
        self.engine = self.device = ''

    def run(self, request, timeout):
        if not self.lock.acquire(blocking=False):
            raise RuntimeError('Chatterbox is busy with another voice request')
        try:
            request_id = str(request.get('requestId', ''))[:64]
            with self.cancel_lock:
                self.active_id = request_id
                if request_id and request_id in self.cancelled: raise VoiceCancelled('Voice request cancelled')
            self.started = time.monotonic()
            self.phase = 'starting model process'
            if self.process is None or not self.process.is_alive():
                self.stop()
                context = multiprocessing.get_context('spawn')
                self.pipe, child = context.Pipe()
                self.cancel_event = context.Event() if self.cooperative else None
                self.process = context.Process(target=self.target, args=(child, self.cancel_event) if self.cooperative else (child,), daemon=True)
                self.process.start()
                child.close()
            with self.cancel_lock:
                if self.cancel_event: self.cancel_event.clear()
                if request_id and request_id in self.cancelled: raise VoiceCancelled('Voice request cancelled')
            self.pipe.send(request)
            cancelling_at = None
            while time.monotonic() - self.started < timeout:
                with self.cancel_lock:
                    cancelled = bool(request_id and request_id in self.cancelled)
                if cancelled:
                    if self.cancel_event: self.cancel_event.set()
                    if cancelling_at is None: cancelling_at = time.monotonic()
                    if not self.cooperative or time.monotonic() - cancelling_at > 3:
                        self.stop()
                        raise VoiceCancelled('Voice request cancelled')
                if not self.pipe.poll(.05):
                    if not self.process.is_alive():
                        raise RuntimeError('Chatterbox model process exited')
                    continue
                message = self.pipe.recv()
                if message['kind'] == 'progress':
                    self.phase = message['phase']
                    self.device = message.get('device', '')
                    self.engine = request.get('engine', '')
                elif message['kind'] == 'result':
                    self.phase = 'idle'
                    if cancelled: raise VoiceCancelled('Voice request cancelled')
                    return message['audio']
                elif message['kind'] == 'cancelled':
                    raise VoiceCancelled('Voice request cancelled')
                else:
                    raise RuntimeError(message.get('error', 'Chatterbox failed'))
            phase = self.phase
            self.stop()  # A cancelled Python thread cannot release a stuck CUDA call.
            raise TimeoutError('Chatterbox exceeded its voice deadline while ' + phase)
        except VoiceCancelled:
            self.phase = 'idle'
            raise
        except Exception:
            self.stop()
            self.phase = 'failed'
            raise
        finally:
            with self.cancel_lock: self.active_id = ''
            self.last_use = time.monotonic()
            with self.lease_lock:
                release = self.release_when_idle and not any(v > time.monotonic() for v in self.leases.values())
            if release:
                self.stop()
                self.phase = 'idle'
                self.release_when_idle = False
            self.lock.release()

    def cancel(self, request_id):
        if not isinstance(request_id, str) or not 1 <= len(request_id) <= 64: raise ValueError('Invalid voice request identity')
        with self.cancel_lock:
            self.cancelled = {k:v for k,v in self.cancelled.items() if v > time.monotonic()}
            if len(self.cancelled) >= 128: self.cancelled.pop(next(iter(self.cancelled)))
            self.cancelled[request_id] = time.monotonic() + 120
            if self.active_id == request_id and self.cancel_event: self.cancel_event.set()
        # Acknowledge once the old request has relinquished the worker.
        end = time.monotonic() + 5
        while time.monotonic() < end:
            with self.cancel_lock:
                if self.active_id != request_id: return
            time.sleep(.025)

    def lease(self, owner, retain):
        if not isinstance(owner, str) or not 1 <= len(owner) <= 64: raise ValueError('Invalid lease owner')
        with self.lease_lock:
            self.leases = {k:v for k,v in self.leases.items() if v > time.monotonic()}
            if retain:
                if owner not in self.leases and len(self.leases) >= 8: raise ValueError('Too many voice clients')
                self.leases[owner] = time.monotonic() + 90
                self.release_when_idle = False
            else:
                released = self.leases.pop(owner, None) is not None
                if released: self.release_when_idle = True
        if not retain and released: self.unload_idle(-1)

    def unload_idle(self, seconds=120):
        if self.lock.acquire(blocking=False):
            try:
                with self.lease_lock:
                    retained = any(expiry > time.monotonic() for expiry in self.leases.values())
                if not retained and self.process is not None and time.monotonic() - self.last_use > seconds:
                    self.stop()
                    self.phase = 'idle'
                    self.release_when_idle = False
            finally:
                self.lock.release()

    def status(self):
        return dict(loaded=self.engine, device=self.device, phase=self.phase,
                    elapsed=round(time.monotonic() - self.started, 1) if self.lock.locked() else 0)
