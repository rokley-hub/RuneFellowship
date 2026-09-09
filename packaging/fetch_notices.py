from pathlib import Path
import urllib.request, hashlib, json
root=Path(__file__).resolve().parent/'licenses';root.mkdir(exist_ok=True)
sources={
 'Kokoro-model-LICENSE':'https://huggingface.co/hexgrad/Kokoro-82M/raw/main/LICENSE',
 'Kokoro-onnx-LICENSE':'https://raw.githubusercontent.com/thewh1teagle/kokoro-onnx/main/LICENSE',
 'Whisper-LICENSE':'https://raw.githubusercontent.com/openai/whisper/main/LICENSE',
 'Faster-Whisper-LICENSE':'https://raw.githubusercontent.com/SYSTRAN/faster-whisper/master/LICENSE',
 'Chatterbox-LICENSE':'https://raw.githubusercontent.com/resemble-ai/chatterbox/master/LICENSE',
 'Ollama-LICENSE':'https://raw.githubusercontent.com/ollama/ollama/main/LICENSE',
 'Qwen3.5-4B-LICENSE':'https://huggingface.co/Qwen/Qwen3.5-4B/raw/main/LICENSE',
}
results=[]
for name,url in sources.items():
 try:
  content=urllib.request.urlopen(url,timeout=30).read();(root/name).write_bytes(content)
  results.append({'file':name,'url':url,'sha256':hashlib.sha256(content).hexdigest()})
 except Exception as e: results.append({'file':name,'url':url,'error':str(e)})
(root/'upstream-sources.json').write_text(json.dumps(results,indent=2));print(json.dumps(results,indent=2))
