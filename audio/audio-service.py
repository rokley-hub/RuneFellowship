"""Rune local audio: Kokoro voices, Whisper transcription, microphone held in RAM only."""
from pathlib import Path
import sys, io, json, wave, threading, time, collections, argparse, urllib.request, urllib.error, urllib.parse, os
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
root = Path(__file__).resolve().parent
sys.path.insert(0, str(root / 'audio-packages'))
import numpy as np
import sounddevice as sd
import webrtcvad
import onnxruntime as ort
from faster_whisper import WhisperModel
from kokoro_onnx import Kokoro

recognition_threads = min(4, max(1, (os.cpu_count() or 2) // 2))
voice_threads = min(3, recognition_threads)
options = ort.SessionOptions(); options.intra_op_num_threads = voice_threads
kokoro = None
whisper = WhisperModel(str(root / 'whisper-small.en'), device='cpu', compute_type='int8', cpu_threads=recognition_threads, local_files_only=True)
tts_lock = threading.RLock(); transcription_lock = threading.RLock()
LANGUAGES = {'en': 'English', 'de': 'German', 'nl': 'Dutch'}
multilingual = None
speech_pause_ms = 900

def language_model(language, prepare=False):
    global multilingual
    if language not in LANGUAGES: raise ValueError('Choose English, German or Dutch in Conversation language')
    if language == 'en': return whisper
    with transcription_lock:
        if multilingual is None:
            try:
                multilingual = WhisperModel('small', download_root=str(root / 'speech-model-cache'), device='cpu', compute_type='int8', cpu_threads=recognition_threads, local_files_only=not prepare)
            except Exception as e:
                raise ValueError('Prepare multilingual recognition in Settings > Conversation language. ' + str(e)) from e
        return multilingual
VOICES = {'bm_george':'George · British male', 'am_michael':'Michael · American male', 'am_fenrir':'Fenrir · American male', 'bf_emma':'Emma · British female', 'af_heart':'Heart · American female', 'af_bella':'Bella · American female'}
REFERENCE_IDS = ('af_alloy af_aoede af_bella af_heart af_jessica af_kore af_nicole af_nova af_river af_sarah af_sky '
                 'am_adam am_echo am_eric am_fenrir am_liam am_michael am_onyx am_puck am_santa '
                 'bf_alice bf_emma bf_isabella bf_lily bm_daniel bm_fable bm_george bm_lewis').split()
REFERENCE_VOICES = {v: v[3:].capitalize() + ' · ' + ('British' if v.startswith('b') else 'American') + (' male' if v[1] == 'm' else ' female') for v in REFERENCE_IDS}
ENGINES = {'kokoro':'Kokoro · fast and clear', 'chatterbox-turbo':'Chatterbox Turbo · expressive English', 'chatterbox-v3':'Chatterbox V3 · expressive multilingual'}
def voices_for_engine(engine):
    if engine not in ENGINES: raise ValueError('Choose Kokoro, Chatterbox Turbo, or Chatterbox V3')
    return VOICES if engine == 'kokoro' else REFERENCE_VOICES

def kokoro_wav(text, voice):
    global kokoro
    # Internal reference generation may use all 28 source speakers. Public
    # Kokoro requests are restricted to its six choices in synthesize().
    if voice not in REFERENCE_VOICES: raise ValueError('Choose an installed voice')
    with tts_lock:
        if kokoro is None: kokoro = Kokoro.from_session(ort.InferenceSession(str(root / 'kokoro/kokoro-v1.0.onnx'), sess_options=options, providers=['CPUExecutionProvider']), str(root / 'kokoro/voices-v1.0.bin'))
        samples, rate = kokoro.create(text, voice=voice, speed=1.0, lang='en-gb' if voice.startswith('b') else 'en-us')
    data = io.BytesIO()
    with wave.open(data, 'wb') as out:
        out.setnchannels(1); out.setsampwidth(2); out.setframerate(rate)
        out.writeframes((np.clip(samples, -1, 1) * 32767).astype('<i2').tobytes())
    return data.getvalue()

def voice_reference(voice):
    folder=root/'voice-references'; folder.mkdir(exist_ok=True)
    target=folder/(voice+'.wav')
    if not target.exists():
        script='I have crossed cold seas and walked beneath tall pines. Keep your shield close, your fire warm, and never trust a troll near the supplies.'
        temporary=target.with_suffix('.tmp'); temporary.write_bytes(kokoro_wav(script,voice)); temporary.replace(target)
    return target

chatterbox_retry_after = 0.0
chatterbox_last_failure = ""

def synthesize(text, voice, engine='kokoro', language='en', personality='', delivery='natural', intensity=.5, cue='none', allow_fallback=False, metadata=None, request_id=''):
    global chatterbox_retry_after, chatterbox_last_failure
    if engine not in ENGINES: raise ValueError('Choose Kokoro, Chatterbox Turbo, or Chatterbox V3')
    if voice not in voices_for_engine(engine): raise ValueError('Choose a voice available for the selected engine')
    if engine == 'kokoro': return kokoro_wav(text,voice)
    def recovery(reason):
        if not allow_fallback or language != 'en': raise ValueError(reason)
        if metadata is not None: metadata['fallback'] = reason
        # These are the same source speakers used to prepare Chatterbox references.
        # Keep this recovery internal; it does not change the selectable voice lists.
        return kokoro_wav(text, voice)
    if allow_fallback and language == 'en' and time.monotonic() < chatterbox_retry_after:
        return recovery('Cooldown after: ' + chatterbox_last_failure)
    deadline = 15 if allow_fallback else 60
    request=json.dumps({'requestId':request_id,'text':text,'engine':engine,'voice':voice,'language':language,'personality':personality,'delivery':delivery,'intensity':intensity,'cue':cue,'reference':str(voice_reference(voice)), 'timeoutSeconds':deadline}).encode()
    try:
        with urllib.request.urlopen(urllib.request.Request('http://127.0.0.1:11442/tts',data=request,headers={'Content-Type':'application/json'}),timeout=deadline+5) as response: return response.read()
    except urllib.error.HTTPError as error:
        if error.code == 409: raise ValueError('Voice request cancelled') from error
        detail=error.read().decode(errors='replace')
        try: detail=json.loads(detail).get('error',detail)
        except Exception: pass
        chatterbox_retry_after = time.monotonic() + 30
        chatterbox_last_failure = ('Chatterbox: '+str(detail))[:512]
        return recovery(chatterbox_last_failure)
    except (urllib.error.URLError, TimeoutError) as error:
        chatterbox_retry_after = time.monotonic() + 30
        chatterbox_last_failure = ('Voice service unavailable: ' + str(error))[:512] if isinstance(error, urllib.error.URLError) else 'Voice service response timeout'
        return recovery(chatterbox_last_failure)

def transcribe(audio, companion_names='', language='en'):
    with transcription_lock:
        model = language_model(language)
        names = ', '.join(n.strip() for n in companion_names.split(',') if n.strip())
        hotwords = ', '.join(filter(None, [names, 'Rune, Eira, Bjorn, Valheim']))
        prompt = 'Names and vocabulary: ' + hotwords + ', karve, longship, draugr. Casual ' + LANGUAGES[language] + ' conversation and game commands.'
        segments, _ = model.transcribe(audio, language=language, beam_size=5, temperature=0.0, condition_on_previous_text=False, vad_filter=True, initial_prompt=prompt, hotwords=hotwords)
        return ' '.join(s.text.strip() for s in segments if s.no_speech_prob < .75).strip()

class Capture:
    """One input stream; independent FIFO transcription keeps capture running."""
    def __init__(self):
        self.lock=threading.RLock(); self.stream=None; self.state='off'; self.level=0; self.error=''; self.serial=0
        self.events=collections.deque(); self.jobs=collections.deque(); self.worker=False
        self.last_ping=time.monotonic(); self.ended=True; self.has_speech=False
    def reset_frames(self):
        self.frames=[]; self.pre=collections.deque(maxlen=20); self.voiced=0; self.silence=0; self.has_speech=False
    def stop(self):
        with self.lock:
            self.serial += 1; stream=self.stream; self.stream=None; self.state='off'; self.level=0; self.ended=True
            self.reset_frames(); self.events.clear(); self.jobs.clear()
        if stream is not None: stream.abort(); stream.close()
    def queue_utterance(self):
        # Called with lock held. Never discard speech just because Whisper is busy.
        if self.voiced >= 5:
            if len(self.jobs) < 12: self.jobs.append((self.serial,b''.join(self.frames),self.companion_names,self.language))
            else: self.events.append({'error':'Speech queue is full. Please pause briefly and repeat the last sentence.'})
            if not self.worker:
                self.worker=True; threading.Thread(target=self.process_jobs,daemon=True).start()
        self.reset_frames()
    def process_jobs(self):
        while True:
            with self.lock:
                if not self.jobs: self.worker=False; return
                serial,raw,names,language=self.jobs.popleft()
            started=time.monotonic()
            try:
                text=transcribe(np.frombuffer(raw,dtype='<i2').astype(np.float32)/32768,names,language)
                event={'text':text,'seconds':round(time.monotonic()-started,3)}
            except Exception as e: event={'error':str(e)}
            with self.lock:
                if serial == self.serial:
                    self.events.append(event)
                    if not self.automatic: self.state='off'
    def start(self, automatic, device, companion_names='', language='en'):
        language_model(language)
        self.stop()
        with self.lock:
            serial=self.serial; self.reset_frames(); self.vad=webrtcvad.Vad(2)
            self.automatic=automatic; self.companion_names=companion_names; self.language=language; self.ended=False; self.error=''; self.state='listening'; self.last_ping=time.monotonic()
        def callback(data, count, timing, status):
            raw=bytes(data)
            with self.lock:
                if serial != self.serial or self.ended: return
                samples=np.frombuffer(raw,dtype='<i2'); rms=float(np.sqrt(np.mean(samples.astype(np.float32)**2)))/32768
                self.level=min(100,int(rms*900)); speech=self.vad.is_speech(raw,16000)
                if speech: self.voiced += 1; self.silence=0
                else: self.silence += 1
                self.has_speech=self.voiced >= 5
                if self.voiced:
                    if not self.frames: self.frames.extend(self.pre)
                    self.frames.append(raw)
                else: self.pre.append(raw)
                if (automatic and self.voiced >= 5 and self.silence * 30 >= speech_pause_ms) or len(self.frames)>=1000:
                    if automatic: self.queue_utterance()
                    else:
                        self.ended=True; threading.Thread(target=self.finish,args=(serial,),daemon=True).start()
        try:
            stream=sd.RawInputStream(samplerate=16000,blocksize=480,channels=1,dtype='int16',device=None if device < 0 else device,callback=callback)
            with self.lock: self.stream=stream
            stream.start()
        except Exception:
            self.stop(); raise
    def finish(self, serial=None):
        with self.lock:
            if serial is None: serial=self.serial
            if serial != self.serial or self.state != 'listening': return
            self.ended=True; stream=self.stream; self.stream=None; self.level=0; self.state='transcribing'
            if self.voiced < 5: self.events.append({'text':''}); self.state='off'
            else: self.queue_utterance()
        if stream is not None: stream.stop(); stream.close()
    def status(self):
        with self.lock:
            self.last_ping=time.monotonic(); events=list(self.events); self.events.clear()
            return {'state':self.state,'level':self.level,'events':events,'error':self.error,'hasSpeech':self.has_speech,'transcribing':self.worker,'queued':len(self.jobs)}

capture=Capture()
def watchdog():
    while True:
        time.sleep(1)
        if capture.state != 'off' and time.monotonic()-capture.last_ping > 5: capture.stop()

class Handler(BaseHTTPRequestHandler):
    def log_message(self,*args): pass
    def send(self,value,code=200):
        data=json.dumps(value).encode(); self.send_response(code); self.send_header('Content-Type','application/json'); self.send_header('Content-Length',str(len(data))); self.end_headers(); self.wfile.write(data)
    def do_GET(self):
        try:
            if self.path=='/status': self.send(capture.status())
            elif self.path=='/devices': self.send({'devices':[{'id':i,'name':d['name']} for i,d in enumerate(sd.query_devices()) if d['max_input_channels']>0]})
            else: self.send({'ready':True,'speechPauseMs':speech_pause_ms,'version':'0.4.16-cancellation','voices':VOICES,'voicesByEngine':{e: voices_for_engine(e) for e in ENGINES},'engines':ENGINES,'languages':LANGUAGES,'recognizer':'Whisper small.en / small', 'recognition_profile':'explicit language and custom companion names'})
        except Exception as e: self.send({'error':str(e)},400)
    def do_POST(self):
        try:
            # No cross-origin browser microphone control.
            if self.headers.get('Origin'): raise ValueError('Browser requests are not supported')
            length=int(self.headers.get('Content-Length','0'))
            if not 0 < length <= 8192: raise ValueError('Invalid request')
            data=json.loads(self.rfile.read(length))
            if self.path=='/listen': capture.start(bool(data.get('automatic')),int(data.get('device',-1)),str(data.get('companionNames',''))[:160],str(data.get('language','en'))); self.send({'ok':True})
            elif self.path=='/performance':
                global speech_pause_ms
                speech_pause_ms = max(600, min(1500, int(data.get('speechPauseMs', 900))))
                self.send({'ok':True, 'speechPauseMs':speech_pause_ms})
            elif self.path=='/voice/unload':
                global kokoro
                with tts_lock: kokoro = None
                self.send({'ok':True})
            elif self.path=='/warm':
                engine=str(data.get('engine','')); voice=str(data.get('voice','')); language=str(data.get('language','en'))
                if voice not in voices_for_engine(engine): raise ValueError('Choose an installed voice')
                if engine == 'chatterbox-turbo' and language != 'en': raise ValueError('Turbo requires English')
                request=json.dumps({'requestId':str(data.get('requestId','')),'engine':engine,'reference':str(voice_reference(voice))}).encode()
                with urllib.request.urlopen(urllib.request.Request('http://127.0.0.1:11442/warm',data=request,headers={'Content-Type':'application/json'}),timeout=63) as response: response.read()
                self.send({'ok':True})
            elif self.path=='/language/prepare': language_model(str(data.get('language','en')),prepare=True); self.send({'ok':True})
            elif self.path=='/stop': capture.stop(); self.send({'ok':True})
            elif self.path=='/finish': threading.Thread(target=capture.finish,daemon=True).start(); self.send({'ok':True})
            elif self.path=='/tts':
                text=data.get('text','')
                if not isinstance(text,str) or not 0<len(text)<=1000: raise ValueError('Invalid speech text')
                metadata={}
                if data.get('fastVoice') is True and data.get('language','en') == 'en':
                    engine=str(data.get('engine','kokoro')); voice=str(data.get('voice','bm_george'))
                    if voice not in voices_for_engine(engine): raise ValueError('Choose an installed voice')
                    payload=kokoro_wav(text,voice)
                else: payload=synthesize(text,data.get('voice','bm_george'),str(data.get('engine','kokoro')),str(data.get('language','en')),str(data.get('personality',''))[:2000],str(data.get('delivery','natural')),float(data.get('intensity',.5)),str(data.get('cue','none')),data.get('allowFallback') is True,metadata,str(data.get('requestId','')))
                self.send_response(200); self.send_header('Content-Type','audio/wav')
                self.send_header('X-Rune-Voice-Engine', 'kokoro' if metadata.get('fallback') or (data.get('fastVoice') is True and data.get('language','en') == 'en') else str(data.get('engine','kokoro')))
                if metadata.get('fallback'):
                    self.send_header('X-Rune-Voice-Fallback','kokoro')
                    self.send_header('X-Rune-Voice-Fallback-Reason',urllib.parse.quote(str(metadata['fallback'])[:512]))
                self.send_header('Content-Length',str(len(payload))); self.end_headers(); self.wfile.write(payload)
            else: self.send({'error':'Unknown request'},404)
        except (BrokenPipeError,ConnectionResetError): pass
        except (BrokenPipeError,ConnectionResetError,ConnectionAbortedError): pass
        except Exception as e:
            try: self.send({'error':str(e)},400)
            except (BrokenPipeError,ConnectionResetError,ConnectionAbortedError): pass

if __name__=='__main__':
    parser=argparse.ArgumentParser(); parser.add_argument('--self-test'); args=parser.parse_args()
    if args.self_test:
        report={'voices':{},'transcriptions':[]}; output=Path(args.self_test); output.parent.mkdir(exist_ok=True)
        for voice in VOICES:
            wav=synthesize('Hello, friend. We can talk about anything. I have opinions about movies, and suspiciously strong opinions about trees.',voice)
            (output.parent/(voice+'.wav')).write_bytes(wav); report['voices'][voice]=len(wav)
        for text in ['Gather twelve wood.', 'I enjoy science fiction movies. What about you?']:
            wav=synthesize(text,'am_michael'); heard=transcribe(io.BytesIO(wav)); report['transcriptions'].append({'said':text,'heard':heard})
        output.write_text(json.dumps(report,indent=2)); print(json.dumps(report),flush=True)
    else:
        threading.Thread(target=watchdog,daemon=True).start()
        print('Rune local audio ready on 127.0.0.1:11441',flush=True)
        ThreadingHTTPServer(('127.0.0.1',11441),Handler).serve_forever()
