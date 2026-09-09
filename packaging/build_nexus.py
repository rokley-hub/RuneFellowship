"""Build a local Nexus review candidate, never an upload or licensing clearance."""
import argparse, json, os, shutil, subprocess, zipfile
from pathlib import Path
import build_release as b
import package_tools as packages

def build(source, output, app, plugin, version, gameplay):
    source, output = source.resolve(), output.resolve()
    if output.exists(): raise ValueError('Use a new output folder')
    packages.verify(source)
    output.mkdir(parents=True)
    # Copy exactly the already reviewed manifest, never a live installation.
    for row in packages.read_manifest(source)['files']:
        target = packages.safe(output, row['path'])
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(packages.safe(source, row['path']), target)
    for name in ['Setup Rune.ps1', 'SetupLauncher.cs']:
        shutil.copy2(b.HERE/name, output/name)
    b.copy_tree(app.resolve(),output/'app')
    deps=json.loads((app/'RuneVoice.deps.json').read_text())
    if 'RuneVoice/'+version not in deps['libraries']: raise ValueError('Desktop version does not match supplied app')
    shutil.copy2(plugin,output/'payload/BepInEx/plugins/RuneCompanion/RuneCompanion.dll')
    release=json.loads((output/'release.json').read_text())
    release.update(version=version+'-beta',desktop=version,gameplay=gameplay,channel='test-before-publication')
    (output/'release.json').write_text(json.dumps(release,indent=2))
    for name in ['Start Rune.ps1', 'Service Ports.ps1', 'Stop Rune services.ps1']:
        shutil.copy2(b.HERE/name,output/name)
    (output/'TEST THIS BUILD.txt').write_text(
        'Rune '+version+' / gameplay '+gameplay+' - local test build\n\n'
        'Install into a separate empty folder for testing. Your existing Rune installation is not changed.\n'
        'Companions: Show on map saves per companion. Unsummon keeps the saved profile.\n'
        'In-game controls (default F8) also offer Unsummon for the selected companion.\n'
        'Return cargo and borrowed equipment first; personal equipment remains on the ground.\n'
        'KNOWN LIMITATION: PlanBuild has a compatibility error with the currently tested Valheim build.\n'
        'Its author needs to update it; Rune building functionality depending on it is not verified.\n'
        'Qwen is optional and requires its local-brain pack. Kokoro and English recognition are included.\n'
        'This installer does not include your personal profiles, conversations, account login or saves.\n'
        'For testing only. Public distribution review is not complete.\n',encoding='utf-8')
    compiler = Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    subprocess.run([str(compiler), '/nologo', '/target:winexe', '/optimize+',
        '/r:System.Windows.Forms.dll', '/win32icon:'+str(app/'Assets/rune.ico'), '/out:'+str(output/'Install Rune.exe'),
        str(output/'SetupLauncher.cs')], check=True)
    subprocess.run([str(compiler), '/nologo', '/target:winexe', '/optimize+',
        '/r:System.Windows.Forms.dll', '/win32icon:'+str(app/'Assets/rune.ico'), '/out:'+str(output/'Open Rune.exe'),
        str(b.HERE.parent/'Launcher.cs')],check=True)
    # Nexus rejects archives inside archives. Python can load its standard library
    # from a directory; keep the embedded interpreter isolated using its _pth file.
    stdlib = output/'runtime/python/python312.zip'
    with zipfile.ZipFile(stdlib) as archive:
        for entry in archive.infolist():
            if entry.is_dir(): continue
            target = packages.safe(output/'runtime/python/Lib', entry.filename)
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(entry) as src, target.open('wb') as dst:
                shutil.copyfileobj(src, dst)
    stdlib.unlink() # Exact candidate file, never a computed recursive deletion.
    (output/'runtime/python/python312._pth').write_text('Lib\n.\n..\nimport site\n')
    nested = [p.relative_to(output).as_posix() for p in output.rglob('*') if p.suffix.lower() in {'.zip','.7z','.rar'}]
    if nested: raise ValueError('Nested archives: '+str(nested))
    (output/'READ ME FIRST.txt').write_text(
        'Rune Fellowship - installer review candidate\n\n'
        'Extract all files, then double-click Install Rune.exe.\n'
        'Choose a separate destination folder. No administrator access is required for the default location.\n'
        'Kokoro voice and English recognition are included. Optional packs are separate downloads.\n'
        'Open Rune, create a profile in Mods, and install Rune requirements there.\n'
        'This is a standalone app installer; do not install this archive through Vortex.\n\n'
        'REVIEW CANDIDATE: not uploaded. Native dependency redistribution review and Nexus staff review remain pending.\n'
        'Source: https://github.com/rokley-hub/RuneFellowship\n', encoding='utf-8')
    manifest = {'format':1, 'release':json.loads((output/'release.json').read_text())['version'], 'files':[
        {'path':p.relative_to(output).as_posix(),'bytes':p.stat().st_size,'sha256':b.digest(p)}
        for p in sorted(output.rglob('*')) if p.is_file() and p.name != 'files.json']}
    (output/'files.json').write_text(json.dumps(manifest,indent=2))
    packages.verify(output)
    subprocess.run([str(output/'Install Rune.exe'),'--check'],check=True)
    archive_path = output.with_name(output.name+'.zip')
    with zipfile.ZipFile(archive_path, 'w',zipfile.ZIP_DEFLATED,compresslevel=1) as archive:
        for p in sorted(output.rglob('*')):
            if p.is_file(): archive.write(p,p.relative_to(output))
    with zipfile.ZipFile(archive_path) as archive:
        if archive.testzip(): raise ValueError('Archive integrity failed')
    result={'candidate':archive_path.name,'bytes':archive_path.stat().st_size,
        'sha256':b.digest(archive_path),'files':len(manifest['files'])+1,
        'nestedArchives':nested,'publicUploadCleared':False}
    archive_path.with_suffix('.json').write_text(json.dumps(result,indent=2))
    print(json.dumps(result),flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser(); parser.add_argument('--source',type=Path,default=b.CORE)
    parser.add_argument('--output',type=Path,required=True); parser.add_argument('--app',type=Path,required=True)
    parser.add_argument('--plugin',type=Path,required=True); parser.add_argument('--version',required=True); parser.add_argument('--gameplay',required=True); args=parser.parse_args()
    build(args.source,args.output,args.app,args.plugin,args.version,args.gameplay)
