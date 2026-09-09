"""Split a verified offline candidate into a compact core and optional speech pack."""
from pathlib import Path
import argparse,json,shutil,zipfile
import build_release as b
import package_tools as packages

def write_zip(folder,target):
    with zipfile.ZipFile(target,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
        for p in sorted(folder.rglob('*')):
            if p.is_file():z.write(p,p.relative_to(folder))
    with zipfile.ZipFile(target) as z:
        if z.testzip():raise ValueError('Damaged output archive')

def build(source,output):
    source=source.resolve();output=output.resolve()
    manifest=packages.verify(source)
    if output.exists():raise ValueError('Use a new compact output directory')
    output.mkdir(parents=True)
    model_roots=('runtime/kokoro/','runtime/whisper-small.en/')
    models=[r for r in manifest['files'] if r['path'].startswith(model_roots)]
    if not models:raise ValueError('Source has no speech models')
    release=json.loads((source/'release.json').read_text())
    version=release['desktop']
    pack=output.parent/('Rune-speech-models-'+version+'-beta-test.zip')
    if pack.exists():raise ValueError('Speech pack already exists')
    with zipfile.ZipFile(pack,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
        for row in models:z.write(packages.safe(source,row['path']),row['path'])
        z.writestr('pack.json',json.dumps({'format':1,'pack':'speech-core','release':release['version'],'files':models},indent=2))
    with zipfile.ZipFile(pack) as z:
        if z.testzip():raise ValueError('Speech archive verification failed')
    for row in manifest['files']:
        if row['path'].startswith(model_roots):continue
        target=packages.safe(output,row['path']);target.parent.mkdir(parents=True,exist_ok=True)
        shutil.copy2(packages.safe(source,row['path']),target)
    for name in ['Setup Rune.ps1','Start Rune.ps1','Service Ports.ps1','package_tools.py']:shutil.copy2(b.HERE/name,output/name)
    catalog=json.loads((output/'packs.json').read_text())
    catalog['speech-core']={'file':pack.name,'bytes':pack.stat().st_size,'unpackedBytes':sum(r['bytes'] for r in models),'sha256':b.digest(pack)}
    (output/'packs.json').write_text(json.dumps(catalog,indent=2))
    release['variant']='compact-separate-speech-models'
    (output/'release.json').write_text(json.dumps(release,indent=2))
    instructions=('Rune '+version+' - compact test installer\n\n'
        '1. Extract this installer ZIP completely.\n'
        '2. For microphone input and Kokoro voices, also download '+pack.name+'.\n'
        '   Leave that speech ZIP unopened, beside the extracted installer folder.\n'
        '3. Open Install Rune.exe, choose a separate test folder and select Speech models.\n'
        '   The option is grey until the matching speech ZIP is present.\n'
        '4. You can skip speech for typed use. Choose/install a brain in Rune Settings.\n'
        '   Qwen and Chatterbox still require their separate optional packs.\n'
        '5. To add speech later, rerun this installer into the same install folder with the speech ZIP beside it.\n'
        '   Then enable the microphone and Spoken replies in Rune Settings if previously disabled.\n\n'
        'No other authors\' game mods are bundled. Required mods download separately.\n'
        'PlanBuild compatibility awaits its author; no fix is included.\n'
        'Local test build only; nothing is published or cleared for public redistribution.\n')
    (output/'READ ME FIRST.txt').write_text(instructions,encoding='utf-8')
    (output/'TEST THIS BUILD.txt').write_text(instructions,encoding='utf-8')
    guide=output/'docs/START-HERE.md'
    text=guide.read_text(encoding='utf-8-sig').replace('English speech recognition and Kokoro voice.','audio libraries. Speech models are a separate optional download.').replace('English recognition is bundled;','English recognition requires the speech-model pack;')
    guide.write_text(text,encoding='utf-8')
    (output/'files.json').write_text(json.dumps({'format':1,'release':release['version'],'files':[
        {'path':p.relative_to(output).as_posix(),'bytes':p.stat().st_size,'sha256':b.digest(p)}
        for p in sorted(output.rglob('*')) if p.is_file() and p.name!='files.json']},indent=2))
    packages.verify(output)
    archive=output.with_name(output.name+'.zip');write_zip(output,archive)
    result={'file':archive.name,'bytes':archive.stat().st_size,'sha256':b.digest(archive),'speechPack':catalog['speech-core'],'otherAuthorsModsBundled':False,'publicUploadCleared':False}
    archive.with_suffix('.json').write_text(json.dumps(result,indent=2))
    print(json.dumps(result),flush=True)

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--source',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args();build(a.source,a.output)
