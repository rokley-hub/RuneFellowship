"""Build-time source lock. Downloads metadata from the publishers, never user data."""
import argparse, concurrent.futures, hashlib, importlib.metadata, json, re, urllib.request, urllib.parse
from pathlib import Path
from packaging.tags import cpython_tags, compatible_tags
from packaging.utils import parse_wheel_filename
from packaging.requirements import Requirement
from packaging.markers import default_environment

OPENER = urllib.request.build_opener(urllib.request.ProxyHandler({}))
TAGS = list(cpython_tags((3,12), platforms=['win_amd64'])) + list(compatible_tags((3,12), interpreter='cp312', platforms=['win_amd64']))
RANK = {t:i for i,t in enumerate(TAGS)}
def get(url):
    return OPENER.open(urllib.request.Request(url, headers={'User-Agent':'Rune-release-source-check'}),timeout=60).read()
def doc(url):return json.loads(get(url))
def sha(path):
    with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
def wheel(row,group):
    name,version=row['name'],row['version']
    data=doc('https://pypi.org/pypi/'+name+'/'+version+'/json')
    choices=[]
    for f in data['urls']:
        if f['packagetype']!='bdist_wheel' or f.get('yanked'):continue
        tags=parse_wheel_filename(f['filename'])[3]
        ranks=[RANK[t] for t in tags if t in RANK]
        if ranks:choices.append((min(ranks),f))
    if not choices:raise ValueError('No Windows Python 3.12 wheel: '+name+'=='+version)
    f=min(choices,key=lambda x:x[0])[1]
    return dict(name=name+' '+version,group=group,url=f['url'],bytes=f['size'],sha256=f['digests']['sha256'],kind='wheel',target='runtime/'+('audio-packages' if group=='speech' else 'chatterbox-packages'),license=data['info'].get('license_expression') or row.get('license',''),source='https://pypi.org/project/'+name+'/'+version+'/')
def main():
    p=argparse.ArgumentParser();p.add_argument('--runtime',type=Path,required=True);p.add_argument('--inventory',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
    result=[];errors=[]
    rows=json.loads(a.inventory.read_text())
    with concurrent.futures.ThreadPoolExecutor(8) as pool:
        jobs=[pool.submit(wheel,row,'speech') for row in rows]
        for j in jobs:
            try:result.append(j.result())
            except Exception as e:errors.append(str(e))
    # These packages are the installed runtime closure, excluding training/demo UI tools.
    wanted=set('chatterbox-tts torch torchaudio numpy transformers tokenizers diffusers safetensors librosa scipy numba llvmlite s3tokenizer resemble-perth conformer einops omegaconf antlr4-python3-runtime pykakasi jaconv spacy-pkuseg onnx soundfile soxr pyloudnorm audioread decorator pooch lazy-loader scikit-learn joblib threadpoolctl requests charset-normalizer urllib3 certifi cffi pycparser tqdm packaging pyyaml filelock fsspec huggingface-hub hf-xet regex typing-extensions networkx jinja2 markupsafe sympy mpmath importlib-metadata zipp importlib-resources more-itertools python-dateutil six ml-dtypes httpx httpcore h11 anyio idna'.split())
    normalize=lambda s:s.lower().replace('_','-')
    distributions={normalize(d.metadata['Name']):d for d in importlib.metadata.distributions(path=[str(a.runtime/'chatterbox-packages')]) if d.metadata.get('Name')}
    environment=default_environment();environment.update(python_version='3.12',python_full_version='3.12.10',extra='')
    excluded={'gradio','pre-commit'} # Upstream demo UI and contributor tooling are not imported by Rune.
    pending=list(wanted)
    while pending:
        name=pending.pop()
        if name not in distributions:continue
        for raw in distributions[name].requires or []:
            requirement=Requirement(raw);dependency=normalize(requirement.name)
            if requirement.marker and not requirement.marker.evaluate(environment):continue
            if dependency in excluded or dependency in wanted:continue
            wanted.add(dependency);pending.append(dependency)
            if dependency not in distributions:errors.append('Missing installed dependency '+dependency)
    expressive=[{'name':d.metadata['Name'],'version':d.version,'license':d.metadata.get('License-Expression') or ''} for name,d in distributions.items() if name in wanted]
    with concurrent.futures.ThreadPoolExecutor(8) as pool:
        jobs=[pool.submit(wheel,row,'expressive') for row in expressive if row['name'].lower() not in ['torch','torchaudio','antlr4-python3-runtime']]
        for j in jobs:
            try:result.append(j.result())
            except Exception as e:errors.append(str(e))
    for name in ['torch','torchaudio']:
        links=re.findall(r'href="([^"]+)"',get('https://download.pytorch.org/whl/cu124/'+name+'/').decode())
        url=next(u for u in links if name+'-2.6.0%2Bcu124-' in u and 'cp312-cp312-win_amd64' in u)
        url=urllib.parse.urljoin('https://download.pytorch.org',url)
        digest=url.split('#sha256=')[1];url=url.split('#')[0]
        url=url.replace('https://download-r2.pytorch.org/','https://download.pytorch.org/')
        print('Checking '+url,flush=True)
        head=OPENER.open(urllib.request.Request(url,method='HEAD'),timeout=60)
        result.append(dict(name=name+' 2.6.0+cu124',group='expressive',url=url,bytes=int(head.headers['Content-Length']),sha256=digest,kind='wheel',target='runtime/chatterbox-packages',license='BSD-3-Clause',source='https://pytorch.org/'))
    f=doc('https://pypi.org/pypi/antlr4-python3-runtime/4.9.3/json')['urls'][0]
    result.append(dict(name='antlr4-python3-runtime 4.9.3',group='expressive',url=f['url'],bytes=f['size'],sha256=f['digests']['sha256'],kind='pure-source',prefix='antlr4-python3-runtime-4.9.3/src/',target='runtime/chatterbox-packages',license='BSD-3-Clause',source='https://pypi.org/project/antlr4-python3-runtime/4.9.3/'))
    # Runtime ZIPs come directly from their publishers.
    pyurl='https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip'
    py=get(pyurl)
    result.append(dict(name='Python 3.12.10',group='core',url=pyurl,bytes=len(py),sha256=hashlib.sha256(py).hexdigest(),kind='zip',target='runtime/python',license='PSF-2.0',source='https://www.python.org/downloads/release/python-31210/'))
    net=next(r for r in doc('https://builds.dotnet.microsoft.com/dotnet/release-metadata/8.0/releases.json')['releases'] if r['release-version']=='8.0.30')
    runtime=next(f for f in net['runtime']['files'] if f['rid']=='win-x64' and f['name'].endswith('.zip'))
    head=OPENER.open(urllib.request.Request(runtime['url'],method='HEAD'),timeout=60)
    result.append(dict(name='.NET Runtime 8.0.30',group='core',url=runtime['url'],bytes=int(head.headers['Content-Length']),sha512=runtime['hash'].lower(),kind='zip',target='runtime/dotnet',license='MIT',source='https://github.com/dotnet/runtime'))
    f=next(f for f in net['windowsdesktop']['files'] if f['rid']=='win-x64' and f['name'].endswith('.zip'))
    head=OPENER.open(urllib.request.Request(f['url'],method='HEAD'),timeout=60)
    result.append(dict(name='.NET Desktop 8.0.30',group='core',url=f['url'],bytes=int(head.headers['Content-Length']),sha512=f['hash'].lower(),kind='zip',target='runtime/dotnet',license='MIT',source='https://github.com/dotnet/windowsdesktop'))
    assets=doc('https://api.github.com/repos/thewh1teagle/kokoro-onnx/releases/tags/model-files-v1.0')['assets']
    for name in ['kokoro-v1.0.onnx','voices-v1.0.bin']:
        f=next(x for x in assets if x['name']==name)
        result.append(dict(name='Kokoro '+name,group='speech',url=f['browser_download_url'],bytes=f['size'],sha256=sha(a.runtime/'kokoro'/name),kind='file',target='runtime/kokoro/'+name,license='Apache-2.0',source='https://huggingface.co/hexgrad/Kokoro-82M'))
    for repo,folder,group in [('Systran/faster-whisper-small.en','whisper-small.en','speech'),('ResembleAI/chatterbox-turbo','chatterbox-models/turbo','turbo'),('ResembleAI/chatterbox','chatterbox-models/v3','v3')]:
        local=a.runtime/folder
        meta=doc('https://huggingface.co/api/models/'+repo)
        for file in sorted(local.iterdir()):
            if not file.is_file() or file.name.startswith('.'):continue
            revision=meta['sha']
            cached=local/'.cache/huggingface/download'/(file.name+'.metadata')
            if cached.exists():revision=cached.read_text().splitlines()[0]
            result.append(dict(name=group+' '+file.name,group=group,url='https://huggingface.co/'+repo+'/resolve/'+revision+'/'+file.name,bytes=file.stat().st_size,sha256=sha(file),kind='file',target='runtime/'+folder+'/'+file.name,license='MIT',source='https://huggingface.co/'+repo))
    assets=doc('https://api.github.com/repos/ollama/ollama/releases/tags/v0.33.3')['assets']
    f=next(x for x in assets if x['name']=='ollama-windows-amd64.zip')
    result.append(dict(name='Ollama 0.33.3',group='brain',url=f['browser_download_url'],bytes=f['size'],sha256=(f.get('digest') or '').removeprefix('sha256:'),kind='zip',target='runtime/ollama',license='MIT',source='https://github.com/ollama/ollama'))
    m=a.runtime/'models/manifests/registry.ollama.ai/library/qwen3.5/4b'
    manifest=json.loads(m.read_text())
    result.append(dict(name='Qwen 3.5 4B manifest',group='brain',url='https://registry.ollama.ai/v2/library/qwen3.5/manifests/4b',bytes=m.stat().st_size,sha256=sha(m),kind='file',target='runtime/models/manifests/registry.ollama.ai/library/qwen3.5/4b',license='Apache-2.0',source='https://huggingface.co/Qwen/Qwen3.5-4B'))
    for f in [manifest['config']]+manifest['layers']:
        result.append(dict(name='Qwen '+f['mediaType'].rsplit('.',1)[-1],group='brain',url='https://registry.ollama.ai/v2/library/qwen3.5/blobs/'+f['digest'],bytes=f['size'],sha256=f['digest'].split(':')[1],kind='file',target='runtime/models/blobs/'+f['digest'].replace(':','-'),license='Apache-2.0',source='https://huggingface.co/Qwen/Qwen3.5-4B'))
    a.output.write_text(json.dumps({'format':1,'platform':'windows-x64','downloads':result,'errors':errors},indent=2),encoding='utf-8')
    print(json.dumps({'locked':len(result),'errors':errors,'missingHashes':[r['name'] for r in result if not (r.get('sha256') or r.get('sha512'))]}))
if __name__=='__main__':main()
