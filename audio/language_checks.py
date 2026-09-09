"""Test real language-routing functions with fake models; no mic, model download or inference."""
import ast
import threading
from pathlib import Path
from types import SimpleNamespace

source = Path(__file__).with_name('audio-service.py').read_text(encoding='utf-8')
tree = ast.parse(source)
functions = [n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name in ('language_model', 'transcribe')]
calls = []
class Model:
    def transcribe(self, audio, **options):
        calls.append(options)
        return [SimpleNamespace(text=' hello ', no_speech_prob=0.1)], None

loads = []
def load(*args, **kwargs):
    loads.append(kwargs)
    if kwargs['local_files_only']: raise RuntimeError('No cached multilingual model')
    return Model()

scope = dict(root=Path('synthetic'), whisper=Model(), multilingual=None, LANGUAGES={'en':'English','de':'German','nl':'Dutch'}, transcription_lock=threading.RLock(), WhisperModel=load)
exec(compile(ast.Module(body=functions, type_ignores=[]), '<language-functions>', 'exec'), scope)
assert scope['transcribe'](None, 'Rune') == 'hello'
assert calls[-1]['language'] == 'en' and not loads
try: scope['transcribe'](None, 'Rune', 'de'); raise AssertionError('Missing model accepted')
except ValueError as e: assert 'Prepare multilingual' in str(e)
assert loads[-1]['local_files_only']
scope['language_model']('de', prepare=True)
assert not loads[-1]['local_files_only']
scope['transcribe'](None, 'Rune', 'de')
scope['transcribe'](None, 'Rune', 'nl')
assert [c['language'] for c in calls] == ['en', 'de', 'nl']
assert len(loads) == 2
try: scope['transcribe'](None, '', 'auto'); raise AssertionError('Auto detection accepted')
except ValueError: pass
assert "self.companion_names,self.language" in source and "names,language)" in source
assert "str(data.get('language','en'))" in source
print('PASS: English default, explicit German/Dutch routing, no automatic language detection, prepare-only downloads, shared cached model, capture/request language propagation. Synthetic model tests only.')
