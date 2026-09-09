"""Allowlisted, private-data-free Rune beta packaging. Run with Python 3.12+."""
from pathlib import Path
import argparse, hashlib, json, shutil, zipfile, os, importlib.metadata as metadata

HERE = Path(__file__).resolve().parent
WORKSPACE = HERE.parents[2]
SOURCE = WORKSPACE / 'outputs/RuneCompanion'
OUT = WORKSPACE / 'outputs/RuneRelease'
CORE = OUT / 'Rune-0.4.21-beta'
VERSION = '0.4.21-beta'
SKIP = {'__pycache__', '.cache', '.git', 'bin', 'tests', 'test'}

def digest(path):
    with path.open('rb') as f: return hashlib.file_digest(f, 'sha256').hexdigest()

def files(folder):
    def denied(error): raise error
    for base, dirs, names in os.walk(folder, followlinks=False, onerror=denied):
        dirs[:] = sorted(d for d in dirs if d not in SKIP and not Path(base, d).is_symlink())
        for name in sorted(names):
            p = Path(base, name)
            if not p.is_symlink() and p.name != 'direct_url.json' and p.suffix.lower() not in ('.pyc', '.pdb', '.log', '.pid', '.lnk'):
                yield p

def copy_tree(src, dest):
    if not src.is_dir(): raise RuntimeError(f'Missing input: {src}')
    for p in files(src):
        target = dest / p.relative_to(src); target.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(p, target)

def core():
    if CORE.exists(): raise RuntimeError('Use a new build directory; never merge a release with user data.')
    CORE.mkdir(parents=True)
    copy_tree(HERE/'app', CORE/'app')
    copy_tree(SOURCE/'runtime/dotnet', CORE/'runtime/dotnet')
    copy_tree(SOURCE/'runtime/audio-packages', CORE/'runtime/audio-packages')
    for name in ['kokoro', 'whisper-small.en']:
        copy_tree(SOURCE/'runtime'/name, CORE/'runtime'/name)
    with zipfile.ZipFile(HERE/'python-3.12.10.zip') as archive:
        archive.extractall(CORE/'runtime/python')
    # Isolated embedded Python ignores global/user packages, PYTHONPATH and registries.
    (CORE/'runtime/python/python312._pth').write_text('python312.zip\n.\n..\nimport site\n')
    for name in ['audio-service.py', 'chatterbox-service.py', 'voice_worker.py']:
        text = (HERE.parent/'audio'/name).read_text(encoding='utf-8-sig')
        text = text.replace("'http://127.0.0.1:11442/", "'http://127.0.0.1:' + str(int(os.environ.get('RUNE_SERVICE_BASE_PORT', '11439')) + 3) + '/")
        text = text.replace("('127.0.0.1',11441)", "('127.0.0.1',int(os.environ.get('RUNE_SERVICE_BASE_PORT', '11439')) + 2)")
        text = text.replace("('127.0.0.1',11442)", "('127.0.0.1',int(os.environ.get('RUNE_SERVICE_BASE_PORT', '11439')) + 3)")
        (CORE/'runtime'/name).write_text(text, encoding='utf-8')
    plugin = CORE/'payload/BepInEx/plugins/RuneCompanion'; plugin.mkdir(parents=True)
    shutil.copy2(HERE/'plugin/RuneCompanion.dll', plugin)
    copy_tree(HERE.parent/'blueprint-library', CORE/'blueprint-library')
    for name in ['Start Rune.ps1', 'Setup Rune.ps1', 'Stop Rune services.ps1', 'package_tools.py', 'Verify Rune.ps1', 'Rollback Rune.ps1', 'Uninstall Rune.ps1']:
        shutil.copy2(HERE/name, CORE/name)
    shutil.copy2(SOURCE/'Open Rune.exe', CORE/'Open Rune.exe')
    for name in ['Setup', 'Verify', 'Rollback', 'Uninstall']:
        (CORE/f'{name} Rune.cmd').write_text(f'@echo off\r\npowershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0{name} Rune.ps1"\r\nif errorlevel 1 pause\r\n')
    copy_tree(HERE/'docs', CORE/'docs')
    copy_tree(HERE/'licenses', CORE/'licenses')
    for name in ['LICENSE', 'LINKING-EXCEPTION.md']:
        shutil.copy2(HERE/'public'/name, CORE/name)
    inventory(CORE/'runtime/audio-packages', CORE/'docs/audio-components.json')
    (CORE/'release.json').write_text(json.dumps({'version':VERSION,'desktop':'0.4.21','gameplay':'0.3.14','channel':'private-beta','signed':False}, indent=2))
    print('Core staged', flush=True)

def inventory(packages, output):
    result=[]
    for d in metadata.distributions(path=[str(packages)]):
        result.append({'name':d.metadata['Name'], 'version':d.version, 'license':d.metadata.get('License-Expression') or d.metadata.get('License',''), 'home':d.metadata.get('Home-page',''), 'requirements':d.requires or []})
    output.parent.mkdir(parents=True,exist_ok=True); output.write_text(json.dumps(result,indent=2))

def archive_pack(identifier, roots, standalone=None):
    target=OUT/f'Rune-{identifier}-{VERSION}.zip'
    manifest={'format':1,'pack':identifier,'release':VERSION,'files':[]}
    with zipfile.ZipFile(target.with_suffix('.zip.tmp'), 'w', zipfile.ZIP_DEFLATED, compresslevel=1, allowZip64=True) as z:
        entries=[]
        for src, dest in roots:
            entries.extend((p, str(Path(dest)/p.relative_to(src)).replace('\\','/')) for p in files(src))
        entries.extend(standalone or [])
        for p, relative in entries:
            manifest['files'].append({'path':relative,'bytes':p.stat().st_size,'sha256':digest(p)})
            z.write(p,relative)
        z.writestr('pack.json',json.dumps(manifest,indent=2))
    os.replace(target.with_suffix('.zip.tmp'),target)
    record={'file':target.name,'bytes':target.stat().st_size,'unpackedBytes':sum(f['bytes'] for f in manifest['files']),'sha256':digest(target)}
    (OUT/f'{identifier}.json').write_text(json.dumps(record,indent=2))
    print(json.dumps(record),flush=True)

def packs():
    # Qwen only: do not redistribute the obsolete Llama download.
    model=SOURCE/'runtime/models'; m=model/'manifests/registry.ollama.ai/library/qwen3.5/4b'
    doc=json.loads(m.read_text()); blobs=[doc['config']]+doc['layers']
    standalone=[(m,'runtime/models/manifests/registry.ollama.ai/library/qwen3.5/4b')]
    standalone += [(model/'blobs'/x['digest'].replace(':','-'),'runtime/models/blobs/'+x['digest'].replace(':','-')) for x in blobs]
    archive_pack('local-brain',[(SOURCE/'runtime/ollama','runtime/ollama')],standalone)
    archive_pack('expressive-runtime',[(SOURCE/'runtime/chatterbox-packages','runtime/chatterbox-packages')])
    for engine in ['turbo','v3']:
        archive_pack('chatterbox-'+engine,[(SOURCE/'runtime/chatterbox-models'/engine,'runtime/chatterbox-models/'+engine)])

def seal(make_archive=True):
    records={p.stem:json.loads(p.read_text()) for p in OUT.glob('*.json') if p.stem in ['local-brain','expressive-runtime','chatterbox-turbo','chatterbox-v3']}
    (CORE/'packs.json').write_text(json.dumps(records,indent=2))
    forbidden={'bridge','mod-profiles','installation-backups','chatgpt-account','memories','planner-workspace','source','checks'}
    entries=[]
    for p in files(CORE):
        relative=p.relative_to(CORE)
        if relative.parts[0] in forbidden or p.name.lower() in ['auth.json','preferences.json','game-folder.txt']: raise RuntimeError('Private data in release: '+str(relative))
        if p.name == 'files.json': continue
        entries.append({'path':relative.as_posix(),'bytes':p.stat().st_size,'sha256':digest(p)})
    (CORE/'files.json').write_text(json.dumps({'format':1,'release':VERSION,'files':entries},indent=2))
    if make_archive: archive_pack('core',[(CORE,'')])
    print('Sealed allowlisted core; no personal bridge, mods, credentials or saves.',flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('action',choices=['core','packs','seal','manifest']); a=p.parse_args()
    OUT.mkdir(parents=True,exist_ok=True)
    {'core':core,'packs':packs,'seal':seal,'manifest':lambda:seal(False)}[a.action]()
