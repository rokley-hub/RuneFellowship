"""Build a small Rune-authored app/plugin update from a sealed clean installer core.

Publish BOTH the resulting ZIP and .zip.sha256 to the matching GitHub release tag.
Optional runtimes, models and all user data are excluded. Does not upload anything.
"""
import argparse, json, zipfile
from pathlib import Path
import package_tools as p

ROOT_FILES = {'release.json','Rune.exe','Open Rune.exe','Start Rune.ps1','Stop Rune services.ps1','Service Ports.ps1','Verify Rune.ps1','Rollback Rune.ps1','Update Recovery.ps1','Uninstall Rune.ps1','package_tools.py','LICENSE','LINKING-EXCEPTION.md'}
EXACT = {'payload/BepInEx/plugins/RuneCompanion/RuneCompanion.dll','runtime/audio-service.py','runtime/chatterbox-service.py','runtime/voice_worker.py'}

def build(core, output):
    core = core.resolve(); output = output.resolve()
    source = p.verify(core)
    release = json.loads((core/'release.json').read_text(encoding='utf-8-sig'))
    version = release['version']
    import re
    if not re.fullmatch(r'\d+\.\d+\.\d+(-beta)?', version): raise ValueError('Invalid version')
    files = [f for f in source['files'] if f['path'].startswith(('app/','licenses/','docs/')) or f['path'] in ROOT_FILES | EXACT]
    required = {'app/RuneVoice.dll','app/RuneVoice.runtimeconfig.json','release.json','payload/BepInEx/plugins/RuneCompanion/RuneCompanion.dll'}
    if not required.issubset({f['path'] for f in files}): raise ValueError('Missing app or plugin')
    manifest = {'format':1,'desktop':release['desktop'],'gameplay':release['gameplay'],'files':files}
    output.mkdir(parents=True,exist_ok=True)
    archive = output/f'Rune-Update-{version}.zip'
    if archive.exists(): raise ValueError('Use a new update output path')
    with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
        z.writestr('update.json',json.dumps(manifest,indent=2))
        for f in files: z.write(p.safe(core,f['path']),f['path'])
    checksum = archive.with_suffix('.zip.sha256')
    checksum.write_text(p.sha(archive)+'  '+archive.name+'\n',encoding='ascii')
    print(json.dumps({'file':str(archive),'bytes':archive.stat().st_size,'sha256':p.sha(archive),'files':len(files),'published':False}))
    return archive

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--core',type=Path,required=True);parser.add_argument('--output',type=Path,required=True);args=parser.parse_args();build(args.core,args.output)

