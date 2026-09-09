"""Deadline/process recovery and fallback routing checks; no GPU or audio device."""
import ast
import io
import json
import threading
import time
import urllib.error
import urllib.request
from pathlib import Path
from types import SimpleNamespace
from voice_worker import BoundedVoiceWorker


def fake_model(pipe):
    while True:
        request = pipe.recv()
        pipe.send(dict(kind='progress', phase='generating speech', device='test'))
        if request.get('stall'):
            time.sleep(60)
        pipe.send(dict(kind='result', audio=b'wave'))


def run():
    worker = BoundedVoiceWorker(fake_model)
    try:
        started = time.monotonic()
        try:
            worker.run(dict(stall=True), .5)
            raise AssertionError('Deadline not enforced')
        except TimeoutError:
            pass
        assert time.monotonic() - started < 4 and worker.process is None
        assert worker.run({}, 3) == b'wave'
        process = worker.process
        assert worker.run({}, 3) == b'wave' and worker.process is process
        worker.lease('test-client', True)
        worker.unload_idle(-1)
        assert worker.process is process
        worker.lease('another-client', False)
        assert worker.process is process
        worker.lease('test-client', False)
        assert worker.process is None
        worker.run({}, 3)
        worker.lease('expired-client', True)
        worker.leases['expired-client'] = time.monotonic() - 1
        worker.unload_idle(-1)
        assert worker.process is None
        worker.run({}, 3)
        worker.lock.acquire()
        try:
            try:
                worker.run({}, 1)
                raise AssertionError('Concurrent request queued')
            except RuntimeError as error:
                assert 'busy' in str(error)
        finally:
            worker.lock.release()
        worker.unload_idle(-1)
        assert worker.process is None
    finally:
        worker.stop()

    source = Path(__file__).with_name('audio-service.py').read_text(encoding='utf-8')
    tree = ast.parse(source)
    functions = [n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name == 'synthesize']
    requests = []
    def failing(request, **options):
        requests.append(json.loads(request.data))
        raise urllib.error.HTTPError('http://local', 503, 'timeout', {}, io.BytesIO(b'{"error":"deadline"}'))
    fake_url = SimpleNamespace(request=SimpleNamespace(Request=urllib.request.Request, urlopen=failing), error=urllib.error)
    scope = dict(ENGINES={'kokoro','chatterbox-turbo','chatterbox-v3'}, voices_for_engine=lambda e: {'am_onyx'},
                 kokoro_wav=lambda text,voice: b'fast:'+voice.encode(), voice_reference=lambda voice: Path('reference'),
                 chatterbox_retry_after=0., time=time, json=json, urllib=fake_url)
    exec(compile(ast.Module(body=functions,type_ignores=[]), 'synthesize', 'exec'), scope)
    synth = scope['synthesize']
    metadata = {}
    assert synth('hello','am_onyx','chatterbox-turbo',allow_fallback=True,metadata=metadata) == b'fast:am_onyx'
    assert metadata['fallback'] and requests[-1]['timeoutSeconds'] == 15
    assert 'deadline' in metadata['fallback']
    count = len(requests)
    cooldown_metadata = {}
    synth('next sentence','am_onyx','chatterbox-turbo',allow_fallback=True,metadata=cooldown_metadata)
    assert 'Cooldown after:' in cooldown_metadata['fallback'] and 'deadline' in cooldown_metadata['fallback']
    assert len(requests) == count  # Cooldown prevents every sentence stalling again.
    for language, allowed in [('en',False),('de',True)]:
        try:
            synth('sample','am_onyx','chatterbox-v3',language,allow_fallback=allowed)
            raise AssertionError('Audition or unsupported recovery language silently fell back')
        except ValueError:
            pass
    assert requests[-2]['timeoutSeconds'] == 60
    print(json.dumps(dict(passed=True, hardDeadline=True, modelProcessReclaimed=True,
        nextRequestRecovers=True, warmProcessReused=True, concurrentQueueRejected=True,
        activeLeaseRetainsModel=True, closeReleasesModel=True, crashLeaseExpires=True, unrelatedClientCannotReleaseModel=True,
        idleProcessReleased=True, sameReferenceVoiceRecovery=True, cooldown=True, originalFailureReasonRetained=True,
        auditionNeverSubstituted=True, germanNeverFallsBackToEnglish=True)))


if __name__ == '__main__':
    run()
