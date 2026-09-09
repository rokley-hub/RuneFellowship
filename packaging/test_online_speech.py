"""Exercise downloaded speech libraries and weights without microphone access or playback."""
import argparse,importlib.util,json,sys,time,wave
from pathlib import Path
def main():
    p=argparse.ArgumentParser();p.add_argument('installation',type=Path);p.add_argument('output',type=Path);a=p.parse_args()
    runtime=a.installation.resolve()/'runtime';sys.path.insert(0,str(runtime));a.output.mkdir(parents=True,exist_ok=True)
    spec=importlib.util.spec_from_file_location('rune_audio_fixture',runtime/'audio-service.py');module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    start=time.monotonic();data=module.kokoro_wav('The fire is warm. We made it home!','af_bella')
    path=a.output/'kokoro-download-test.wav';path.write_bytes(data)
    with wave.open(str(path),'rb') as wav:duration=wav.getnframes()/wav.getframerate()
    segments,_=module.whisper.transcribe(str(path),language='en');text=' '.join(s.text for s in segments).strip()
    result={'passed':duration>1 and 'fire' in text.lower() and 'warm' in text.lower(),'audioSeconds':round(duration,2),'elapsedSeconds':round(time.monotonic()-start,2),'transcript':text,'input':'Synthetic fixture; no microphone or audio playback.'}
    (a.output/'speech-result.json').write_text(json.dumps(result,indent=2));print(json.dumps(result));return 0 if result['passed'] else 1
if __name__=='__main__':sys.exit(main())
