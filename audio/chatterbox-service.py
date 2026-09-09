"""Isolated Chatterbox worker for Rune. Loads one expressive model at a time."""
from pathlib import Path
import os, sys, io, json, wave, gc, threading, argparse, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from voice_worker import BoundedVoiceWorker, VoiceCancelled

root = Path(__file__).resolve().parent
sys.path.insert(0, str(root / 'chatterbox-packages'))
os.environ.setdefault('HF_HOME', str(root / 'chatterbox-cache'))
os.environ.setdefault('HF_HUB_OFFLINE', '1')

lock = threading.RLock()
active_engine = None
active_model = None
active_device = None
last_use = 0.0
active_reference = None
report_progress = lambda phase: None
check_cancelled = lambda *args: None

def model_folder(engine):
    return root / 'chatterbox-models' / ('turbo' if engine == 'chatterbox-turbo' else 'v3')

def unload_model():
    global active_engine, active_model, active_device, active_reference
    active_engine = active_model = active_device = None
    active_reference = None
    gc.collect()
    try:
        import torch
        if torch.cuda.is_available(): torch.cuda.empty_cache()
    except Exception:
        pass

def load_model(engine, force_cpu=False):
    global active_engine, active_model, active_device
    if engine not in ('chatterbox-turbo', 'chatterbox-v3'): raise ValueError('Unknown expressive voice engine')
    import torch
    device = 'cpu' if force_cpu or not torch.cuda.is_available() else 'cuda'
    if active_engine == engine and active_device == device and active_model is not None: return active_model
    unload_model()
    folder = model_folder(engine)
    if not folder.exists(): raise ValueError('This Chatterbox model is not prepared. Reinstall the Rune voice update.')
    if engine == 'chatterbox-turbo':
        from chatterbox.tts_turbo import ChatterboxTurboTTS
        model = ChatterboxTurboTTS.from_local(folder, device=device)
    else:
        # Rune exposes English, German, and Dutch. Chatterbox otherwise initializes
        # a Chinese segmenter (and tries to download its data) even for these
        # languages, which makes an offline launch slow and brittle.
        from tokenizers import Tokenizer
        from chatterbox.models.tokenizers import MTLTokenizer
        def supported_language_init(tokenizer, vocab_file_path):
            tokenizer.tokenizer = Tokenizer.from_file(str(vocab_file_path))
            tokenizer.cangjie_converter = None
            tokenizer.check_vocabset_sot_eot()
        MTLTokenizer.__init__ = supported_language_init
        from chatterbox.mtl_tts import ChatterboxMultilingualTTS
        model = ChatterboxMultilingualTTS.from_local(folder, device=device, t3_model='v3')
    active_engine, active_model, active_device = engine, model, device
    return model

def add_turbo_cue(text, cue):
    # Cues are selected by Rune's bounded expression mapper, never copied from profile text.
    tag = {'chuckle':'[chuckle]', 'laugh':'[laugh]', 'sigh':'[sigh]'}.get(cue)
    return text if not tag or tag in text else text.rstrip() + ' ' + tag

def generate(request):
    global last_use, active_reference
    engine = str(request.get('engine', ''))
    if request.get('prepareOnly'):
        report_progress('loading voice model')
        model = load_model(engine)
        if request.get('reference'):
            reference = Path(str(request['reference'])).resolve()
            if reference.parent != (root / 'voice-references').resolve() or not reference.exists(): raise ValueError('Companion voice reference is missing')
            key = (str(reference), reference.stat().st_mtime_ns, engine, .5 if engine == 'chatterbox-v3' else 0)
            if active_reference != key:
                report_progress('preparing companion voice')
                model.prepare_conditionals(str(reference), exaggeration=.56 if engine == 'chatterbox-v3' else 0)
                active_reference = key
        report_progress('voice model ready')
        return b''
    language = str(request.get('language', 'en')).lower()
    if engine == 'chatterbox-turbo' and language != 'en': raise ValueError('Chatterbox Turbo speaks English only. Choose Chatterbox V3 for this conversation language.')
    if engine == 'chatterbox-v3' and language not in ('en','de','nl'): raise ValueError('Chatterbox V3 does not support the selected Rune language.')
    reference = Path(str(request.get('reference', ''))).resolve()
    reference_root = (root / 'voice-references').resolve()
    if reference.parent != reference_root or not reference.exists(): raise ValueError('Companion voice reference is missing')
    text = str(request.get('text', '')).strip()
    intensity = max(0.0, min(1.0, float(request.get('intensity', .5))))

    def run(force_cpu=False):
        global active_reference
        report_progress('loading voice model')
        model = load_model(engine, force_cpu)
        reference_key = (str(reference), reference.stat().st_mtime_ns, engine, intensity if engine == 'chatterbox-v3' else 0)
        if active_reference != reference_key:
            report_progress('preparing companion voice')
            model.prepare_conditionals(str(reference), exaggeration=.32 + intensity * .48 if engine == 'chatterbox-v3' else 0)
            active_reference = reference_key
        report_progress('generating speech')
        hook = model.t3.tfmr.register_forward_pre_hook(check_cancelled)
        try:
            if engine == 'chatterbox-turbo':
                wav = model.generate(add_turbo_cue(text, str(request.get('cue','none'))))
            else:
                exaggeration = .32 + intensity * .48
                delivery = str(request.get('delivery','natural'))
                cfg = .32 if delivery in ('energetic','urgent','pleased','amused') else .42
                wav = model.generate(text, language_id=language, exaggeration=exaggeration, cfg_weight=cfg)
            check_cancelled()
        finally: hook.remove()
        samples = wav.squeeze().detach().float().cpu().numpy()
        return samples, int(model.sr)

    # Never silently restart the large model on CPU after a GPU allocation failure.
    # The audio service can provide a fast, clearly labelled recovery voice instead.
    samples, rate = run(False)

    import numpy as np
    data = io.BytesIO()
    with wave.open(data, 'wb') as output:
        output.setnchannels(1); output.setsampwidth(2); output.setframerate(rate)
        output.writeframes((np.clip(samples, -1, 1) * 32767).astype('<i2').tobytes())
    last_use = time.monotonic()
    return data.getvalue()

def model_process(pipe, cancel_event):
    global report_progress, check_cancelled
    def check(*args):
        if cancel_event.is_set(): raise VoiceCancelled('Voice request cancelled')
    check_cancelled = check
    def progress(phase):
        check()
        pipe.send(dict(kind='progress', phase=phase, device=active_device or ''))
    report_progress = progress
    while True:
        try:
            request = pipe.recv()
        except EOFError:
            return
        try:
            pipe.send(dict(kind='result', audio=generate(request)))
        except VoiceCancelled:
            pipe.send(dict(kind='cancelled'))
        except Exception as error:
            pipe.send(dict(kind='error', error=str(error)))

worker = BoundedVoiceWorker(model_process, cooperative=True)

def idle_unloader():
    # Let back-to-back sentences reuse the model, then return VRAM to Qwen/Valheim.
    while True:
        time.sleep(15)
        worker.unload_idle()

class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args): pass
    def json(self, value, code=200):
        data=json.dumps(value).encode(); self.send_response(code); self.send_header('Content-Type','application/json'); self.send_header('Content-Length',str(len(data))); self.end_headers(); self.wfile.write(data)
    def do_GET(self):
        self.json({'ready':True,'version':'0.4.16-cancellation', **worker.status(), 'engines':['chatterbox-turbo','chatterbox-v3']})
    def do_POST(self):
        try:
            if self.headers.get('Origin'): raise ValueError('Browser requests are not supported')
            if self.path not in ('/tts','/warm','/lease','/cancel','/unload'): self.json({'error':'Unknown request'},404); return
            length=int(self.headers.get('Content-Length','0'))
            if not 0 < length <= 16384: raise ValueError('Invalid request')
            request=json.loads(self.rfile.read(length))
            if self.path == '/unload':
                worker.unload_idle(-1); self.json({'ok':True}); return
            if self.path == '/cancel':
                worker.cancel(request.get('requestId')); self.json({'ok':True}); return
            if self.path == '/lease':
                worker.lease(request.get('owner'), request.get('retain') is True); self.json({'ok':True}); return
            request['prepareOnly'] = self.path == '/warm'
            if request['prepareOnly']: request['timeoutSeconds'] = 60
            payload=worker.run(request, max(1, min(60, float(request.get('timeoutSeconds',15)))))
            self.send_response(200); self.send_header('Content-Type','audio/wav'); self.send_header('X-Rune-Voice-Device',worker.device); self.send_header('Content-Length',str(len(payload))); self.end_headers(); self.wfile.write(payload)
        except VoiceCancelled:
            try: self.json({'error':'Voice request cancelled'},409)
            except (BrokenPipeError,ConnectionResetError,ConnectionAbortedError): pass
        except (BrokenPipeError,ConnectionResetError,ConnectionAbortedError): pass
        except Exception as error:
            try: self.json({'error':str(error)},503)
            except (BrokenPipeError,ConnectionResetError,ConnectionAbortedError): pass

if __name__ == '__main__':
    parser=argparse.ArgumentParser(); parser.add_argument('--check',action='store_true'); args=parser.parse_args()
    if args.check:
        import torch, chatterbox
        print(json.dumps({'ok':True,'torch':torch.__version__,'cuda':torch.cuda.is_available(),'package':str(Path(chatterbox.__file__).resolve())}))
    else:
        print('Rune Chatterbox worker ready on 127.0.0.1:11442',flush=True)
        threading.Thread(target=idle_unloader, daemon=True).start()
        ThreadingHTTPServer(('127.0.0.1',11442),Handler).serve_forever()
