"""Finalize player-facing documentation of a verified online beta; never uploads."""
import argparse, json, shutil, zipfile
from pathlib import Path
import package_tools as packages
from build_online import seal

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--speech-notices', type=Path, required=True, help='Official System.Speech 8.0.0 NuGet package folder')
    args = parser.parse_args()
    source, out = args.source.resolve(), args.output.resolve()
    packages.verify(source / 'Rune')
    if out.exists(): raise ValueError('Use a new output directory')
    shutil.copytree(source, out)
    core = out / 'Rune'
    for filename in ['LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT']:
        shutil.copy2(args.speech_notices/filename, core/'licenses'/('System.Speech-'+filename))
    guide = '''# Rune Fellowship 0.4.26 beta

Extract the entire installer ZIP, then open Install Rune.exe. Choose a separate empty installation folder (not the extracted installer folder), or your existing Rune installation when updating. Internet access is required. Select the local speech/brain components you want; setup downloads them from original publishers and verifies their hashes. No separate model ZIPs are needed.

Open Open Rune.exe from the installed folder. Use your own licensed Steam copy of Valheim. Rune's Mods page can create a profile and obtain required mods from their original packages. No other authors' game mods or Valheim game files are included in this download.

IMPORTANT: Keep PlanBuild 0.18.4 disabled on the tested current game version. It failed during player spawning in a live test; disabling it restored normal gameplay. Rune's current requirement setup installs/enables it, so disable it in Mods before launching, including after rerunning requirement setup. Blueprint construction dependent on PlanBuild is unavailable while it is disabled. Its compatibility fix must come from its author.

For optional ChatGPT, install OpenAI's official Codex helper separately and sign in through Rune Settings with your own eligible account. Your account's terms and limits apply. Local Qwen is optional. Microphone recognition and companion voice remain local. Cloud dialogue sends relevant text and game context to the selected provider; read docs/PRIVACY.md.

To add local models later, close Rune and rerun setup into your installed Rune folder. Profiles and conversations are retained. If adding speech to a text-only installation, enable microphone input and spoken replies in Settings.

This is a standalone Windows app installer, not an archive to import into Vortex or another game mod manager. It is an unsigned beta: do not disable Windows security. Testing was on the development PC, not an independent second PC. Default speech and text-only installs were checked; a full fresh online installation of every optional large Qwen/Chatterbox stack remains unverified. Broad mod compatibility, multiplayer and performance on other hardware require field testing. Back up important game saves before beta testing.

Source and releases: https://github.com/rokley-hub/RuneFellowship
Rune's original source is GPLv3-only with the included Valheim/Unity linking exception. Downloaded third-party components retain their own licences. See DOWNLOAD-SOURCES.txt and the included notices. AI-generated code, dialogue, voices and artwork are used; installer art is an illustration, not a gameplay screenshot. Rune is not affiliated with Iron Gate, Coffee Stain, OpenAI or Thunderstore. Nexus review is pending; this release does not claim Nexus approval.
'''
    (out / 'READ ME FIRST.txt').write_text(guide, encoding='utf-8')
    (core / 'docs/START-HERE.md').write_text(guide, encoding='utf-8')
    here = Path(__file__).resolve().parent
    shutil.copy2(here/'docs/ONLINE-LICENCE-REVIEW.md',core/'docs/ONLINE-LICENCE-REVIEW.md')
    for path in [out/'DOWNLOAD-SOURCES.txt',core/'docs/DOWNLOAD-SOURCES.txt']:
        text = path.read_text(encoding='utf-8').replace('Nexus network-tool staff review and final distribution review remain pending. Local test only.', 'Nexus network-tool review is pending. This unsigned beta does not claim Nexus approval. Downloaded third-party components retain their own licences.')
        path.write_text(text, encoding='utf-8')
    # Code, binaries and download locks must be unchanged from the tested installer.
    changed = {'READ ME FIRST.txt','DOWNLOAD-SOURCES.txt','Rune/files.json','Rune/docs/START-HERE.md','Rune/docs/DOWNLOAD-SOURCES.txt','Rune/docs/ONLINE-LICENCE-REVIEW.md'}
    for path in source.rglob('*'):
        if path.is_file() and path.relative_to(source).as_posix() not in changed:
            assert packages.sha(path) == packages.sha(out/path.relative_to(source))
    seal(core)
    packages.verify(core)
    archive = out.with_name(out.name+'.zip')
    with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=9) as z:
        for path in sorted(out.rglob('*')):
            if path.is_file(): z.write(path,path.relative_to(out))
    with zipfile.ZipFile(archive) as z: assert z.testzip() is None
    report = {'file':archive.name,'bytes':archive.stat().st_size,'sha256':packages.sha(archive),'codeIdenticalToTestedR3':True,'bundledOtherAuthorsMods':False,'published':False}
    archive.with_suffix('.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report))

if __name__ == '__main__': main()
