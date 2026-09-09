"""Build the small original-source installer; no upload and no bundled third-party runtime."""
import argparse,json,os,shutil,subprocess,zipfile
from pathlib import Path
import package_tools as p

HERE=Path(__file__).resolve().parent
def seal(root):
    (root/'files.json').write_text(json.dumps({'format':1,'release':'0.4.26-beta','files':[{'path':f.relative_to(root).as_posix(),'bytes':f.stat().st_size,'sha256':p.sha(f)} for f in sorted(root.rglob('*')) if f.is_file() and f.name!='files.json']},indent=2))
def build(source,out):
    source=source.resolve();out=out.resolve()
    if out.exists():raise ValueError('Use a new output folder')
    files=p.verify(source)['files'];out.mkdir(parents=True);core=out/'Rune';core.mkdir()
    for row in files:
        path=row['path']
        # Keep only Rune-authored code/assets, the reviewed notices, and our gameplay DLL.
        if path.startswith('runtime/') and path not in ['runtime/audio-service.py','runtime/chatterbox-service.py','runtime/voice_worker.py']:continue
        if path in ['Install Rune.exe','SetupLauncher.cs','Setup Rune.ps1','Setup Rune.cmd','packs.json','READ ME FIRST.txt','TEST THIS BUILD.txt']:continue
        dest=p.safe(core,path);dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p.safe(source,path),dest)
    (core/'packs.json').write_text('{}')
    release=json.loads((core/'release.json').read_text());release['variant']='online-original-sources';(core/'release.json').write_text(json.dumps(release,indent=2))
    for name in ['Start Rune.ps1','Service Ports.ps1','package_tools.py']:shutil.copy2(HERE/name,core/name)
    for name in ['Online Setup.ps1','Setup Worker.ps1','online_installer.py','online-downloads.json','package_tools.py']:shutil.copy2(HERE/name,out/name)
    (out/'Online Setup.ps1').write_text((HERE/'Online Setup.ps1').read_text(encoding='utf-8-sig'),encoding='utf-8-sig')
    shutil.copytree(HERE/'online-assets',out/'online-assets')
    lock=json.loads((out/'online-downloads.json').read_text())
    if lock.get('errors'):raise ValueError('Unresolved download locks')
    notices=['Rune Fellowship 0.4.26 - original-source downloads','',
        'Setup downloads only the components you select, directly from the publishers below.',
        'Licence information is supplied for attribution, not as a guarantee of legal clearance.',
        'Rune source: GPLv3 with the Valheim/Unity linking exception. See Rune/LICENSE.',
        'No third-party game mods are bundled. Use your own licensed Valheim installation.',
        'The artwork is newly AI-generated for Rune; this project is not affiliated with Iron Gate.',
        'Nexus network-tool staff review and final distribution review remain pending. Local test only.','']
    for row in lock['downloads']:
        notices += [row['name']+' | '+str(row.get('license','See upstream licence')),row['source'],row['url'],'']
    (out/'DOWNLOAD-SOURCES.txt').write_text('\n'.join(notices),encoding='utf-8')
    shutil.copy2(out/'DOWNLOAD-SOURCES.txt',core/'docs/DOWNLOAD-SOURCES.txt')
    shutil.copy2(HERE/'docs/ONLINE-LICENCE-REVIEW.md',core/'docs/ONLINE-LICENCE-REVIEW.md')
    (core/'docs/START-HERE.md').write_text('# Start Rune\n\nOpen `Open Rune.exe` from the installed folder. Use Mods to create your profile and download Rune requirements from their original packages. Other authors\' mods and Valheim are not bundled.\n\nThe online installer downloads the selected voice and local-brain components. Rerun it to add choices later; no separate model ZIPs are needed. Your profiles and conversations are retained. After adding speech to a text-only installation, turn on the microphone and spoken replies in Settings.\n\nFor ChatGPT, install OpenAI\'s Codex helper separately and use your own eligible sign-in in Rune Settings. Your account\'s limits apply. Qwen is optional and is not needed for ChatGPT mode. Speech remains local. Rune is not affiliated with OpenAI or Iron Gate.\n\nPlanBuild currently requires an update from its author for compatibility with the tested game version. Rune building dependent on it is not verified. This remains a local beta for author testing, not a cleared Nexus release.\n',encoding='utf-8')
    (out/'READ ME FIRST.txt').write_text('Rune Fellowship 0.4.26 - online test installer\n\nExtract this entire ZIP. Open Install Rune.exe. Choose a separate test folder and the voices/local brain you want. Setup downloads from the original sources; you do not need separate model ZIPs. Internet is required.\n\nNo other authors\' game mods are included. Install required mods from their original packages using Rune. PlanBuild compatibility currently depends on its author updating it.\n\nChatGPT requires your own sign-in. Local Qwen is optional. The voice remains local.\n\nTo add a model later, rerun this installer into your existing Rune folder. Profiles and conversations are retained. When adding speech to a text-only installation, enable the microphone and spoken replies in Rune Settings.\n\nThis is a standalone app installer, not a Vortex mod archive. Local test only: no public upload has been made.\n',encoding='utf-8')
    launcher=(HERE/'SetupLauncher.cs').read_text().replace('"Setup Rune.ps1", "package_tools.py", "files.json", "runtime\\\\python\\\\python.exe"','"Online Setup.ps1", "online_installer.py", "online-downloads.json", "Rune\\\\files.json"').replace('Setup Rune.ps1','Online Setup.ps1')
    launcher=launcher.replace('"Online Setup.ps1", "online_installer.py"','"Online Setup.ps1", "Setup Worker.ps1", "online_installer.py"')
    (out/'SetupLauncher.cs').write_text(launcher)
    compiler=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    subprocess.run([str(compiler),'/nologo','/target:winexe','/optimize+','/r:System.Windows.Forms.dll','/win32icon:'+str(core/'app/Assets/rune.ico'),'/out:'+str(out/'Install Rune.exe'),str(out/'SetupLauncher.cs')],check=True)
    (out/'Setup Rune.cmd').write_text('@echo off\r\npowershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File "%~dp0Online Setup.ps1"\r\nif errorlevel 1 pause\r\n')
    seal(core);p.verify(core);subprocess.run([str(out/'Install Rune.exe'),'--check'],check=True)
    archive=out.with_name(out.name+'.zip')
    with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
        for file in sorted(out.rglob('*')):
            if file.is_file():z.write(file,file.relative_to(out))
    with zipfile.ZipFile(archive) as z:
        if z.testzip():raise ValueError('Archive integrity failed')
    result={'file':archive.name,'bytes':archive.stat().st_size,'sha256':p.sha(archive),'bundledThirdPartyRuntimes':False,'bundledOtherAuthorsMods':False,'publicUploadCleared':False}
    archive.with_suffix('.json').write_text(json.dumps(result,indent=2));print(json.dumps(result))
if __name__=='__main__':
    a=argparse.ArgumentParser();a.add_argument('--source',type=Path,required=True);a.add_argument('--output',type=Path,required=True);args=a.parse_args();build(args.source,args.output)
