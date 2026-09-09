"""Real subprocess cancellation checks with a synthetic, GPU-free model."""
import threading
import time
from voice_worker import BoundedVoiceWorker, VoiceCancelled

def model(pipe, event):
    while True:
        request = pipe.recv()
        pipe.send(dict(kind='progress', phase='generating', device='test'))
        end = time.monotonic() + request.get('duration', 0)
        while time.monotonic() < end and not event.is_set(): time.sleep(.01)
        pipe.send(dict(kind='cancelled') if event.is_set() else dict(kind='result', audio=b'ok'))

def run():
    worker = BoundedVoiceWorker(model, cooperative=True)
    result = []
    def slow():
        try: worker.run(dict(requestId='old', duration=30), 40)
        except VoiceCancelled: result.append('cancelled')
    try:
        assert worker.run(dict(requestId='first'), 3) == b'ok'
        process = worker.process
        thread = threading.Thread(target=slow); thread.start()
        until = time.monotonic() + 3
        while worker.active_id != 'old' and time.monotonic() < until: time.sleep(.01)
        worker.cancel('old'); thread.join(5)
        assert result == ['cancelled'] and not thread.is_alive()
        assert worker.process is process, 'Cancellation unloaded reusable model'
        assert worker.run(dict(requestId='next'), 3) == b'ok'
        worker.cancel('not-started')
        try:
            worker.run(dict(requestId='not-started'), 3)
            raise AssertionError('Cancelled queued job started')
        except VoiceCancelled: pass
        assert worker.run(dict(requestId='unrelated'), 3) == b'ok'
        worker.lease('desktop', True); worker.lease('desktop', False)
        assert worker.process is None
        print('PASS: active cancellation preserves worker, queued cancellation prevents start, stale cancellation leaves new job intact, release unloads model')
    finally: worker.stop()

if __name__ == '__main__': run()
