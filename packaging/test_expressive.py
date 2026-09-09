"""Generate synthetic speech with each installed expressive engine; no playback or microphone."""
import argparse, importlib.util, json, pathlib, shutil, sys, time, wave

def main():
 p=argparse.ArgumentParser();p.add_argument('installation',type=pathlib.Path);p.add_argument('reference',type=pathlib.Path);p.add_argument('output',type=pathlib.Path);a=p.parse_args()
 runtime=a.installation.resolve()/'runtime';sys.path.insert(0,str(runtime))
 refs=runtime/'voice-references';refs.mkdir(exist_ok=True);reference=refs/'test-reference.wav';shutil.copy2(a.reference,reference)
 spec=importlib.util.spec_from_file_location('rune_expressive_test',runtime/'chatterbox-service.py');module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
 a.output.mkdir(parents=True,exist_ok=True);results=[]
 for engine in ['chatterbox-turbo','chatterbox-v3']:
  started=time.monotonic()
  try:
   audio=module.generate({'engine':engine,'text':'The fire is warm. We made it home!','language':'en','reference':str(reference),'delivery':'pleased','intensity':.6,'cue':'chuckle'})
   path=a.output/(engine+'.wav');path.write_bytes(audio)
   with wave.open(str(path),'rb') as wav:duration=wav.getnframes()/wav.getframerate()
   if duration<=.1:raise RuntimeError('Empty voice result')
   results.append({'engine':engine,'passed':True,'generationSeconds':round(time.monotonic()-started,2),'audioSeconds':round(duration,2),'device':module.active_device})
  except Exception as e:results.append({'engine':engine,'passed':False,'error':str(e)})
  finally:module.unload_model()
  print(json.dumps(results[-1]),flush=True)
 (a.output/'results.json').write_text(json.dumps(results,indent=2))
 if not all(r['passed'] for r in results):sys.exit(1)

if __name__=='__main__':main()
