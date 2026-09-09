"""Refresh only authored candidate files before sealing; never use on a live install."""
import build_release as b
if (b.CORE/'bridge').exists(): raise RuntimeError('Refuse to refresh a used release folder')
b.copy_tree(b.HERE/'app',b.CORE/'app')
b.copy_tree(b.HERE.parent/'blueprint-library',b.CORE/'blueprint-library')
b.shutil.copy2(b.HERE/'plugin/RuneCompanion.dll',b.CORE/'payload/BepInEx/plugins/RuneCompanion/RuneCompanion.dll')
for name in ['LICENSE','LINKING-EXCEPTION.md']:b.shutil.copy2(b.HERE/'public'/name,b.CORE/name)
for name in ['Start Rune.ps1','Stop Rune services.ps1','Setup Rune.ps1','Verify Rune.ps1','Rollback Rune.ps1','Uninstall Rune.ps1','package_tools.py']:
 b.shutil.copy2(b.HERE/name,b.CORE/name)
for name in ['audio-service.py','chatterbox-service.py','voice_worker.py']:
 text=(b.HERE.parent/'audio'/name).read_text(encoding='utf-8-sig')
 text=text.replace("'http://127.0.0.1:11442/", "'http://127.0.0.1:' + str(int(os.environ.get('RUNE_SERVICE_BASE_PORT', '11439')) + 3) + '/")
 text=text.replace("('127.0.0.1',11441)","('127.0.0.1',int(os.environ.get('RUNE_SERVICE_BASE_PORT', '11439')) + 2)")
 text=text.replace("('127.0.0.1',11442)","('127.0.0.1',int(os.environ.get('RUNE_SERVICE_BASE_PORT', '11439')) + 3)")
 compile(text,name,'exec');(b.CORE/'runtime'/name).write_text(text,encoding='utf-8')
for name in ['docs','licenses']:b.copy_tree(b.HERE/name,b.CORE/name)
b.seal(False)
