import unittest, tempfile, json, zipfile, sys
from pathlib import Path
from unittest.mock import patch
sys.path.insert(0,str(Path(__file__).resolve().parent))
import package_tools as p

class PackagingTests(unittest.TestCase):
 def setUp(self):
  self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name);self.source=self.root/'source';self.target=self.root/'installed';self.source.mkdir()
  (self.source/'release.json').write_text('{"version":"test"}')
  (self.source/'app').mkdir();(self.source/'app/rune.txt').write_text('new version');self.seal()
 def tearDown(self): self.temp.cleanup()
 def seal(self):
  rows=[{'path':f.relative_to(self.source).as_posix(),'bytes':f.stat().st_size,'sha256':p.sha(f)} for f in self.source.rglob('*') if f.is_file() and f.name!='files.json']
  (self.source/'files.json').write_text(json.dumps({'format':1,'release':'test','files':rows}))
 def old(self):
  self.target.mkdir();(self.target/'release.json').write_text('{"version":"old"}');(self.target/'app').mkdir();(self.target/'app/rune.txt').write_text('old version')
  (self.target/'bridge').mkdir();(self.target/'bridge/preferences.json').write_text('personal settings')
 def test_install_update_and_rollback_preserve_data(self):
  self.old();p.install(self.source,self.target)
  self.assertEqual((self.target/'app/rune.txt').read_text(),'new version')
  self.assertEqual((self.target/'bridge/preferences.json').read_text(),'personal settings')
  p.rollback(self.target);self.assertEqual((self.target/'app/rune.txt').read_text(),'old version')
  self.assertEqual((self.target/'bridge/preferences.json').read_text(),'personal settings')
 def test_corruption_rejected_before_mutation(self):
  self.old();(self.source/'app/rune.txt').write_text('corrupt')
  with self.assertRaises(ValueError):p.install(self.source,self.target)
  self.assertEqual((self.target/'app/rune.txt').read_text(),'old version')
 def test_copy_failure_rolls_back(self):
  self.old();original=p.shutil.copy2
  def fail(src,dst,*args,**kwargs):
   if str(dst).find('rune.txt.rune-install-')>=0:raise OSError('simulated disk failure')
   return original(src,dst,*args,**kwargs)
  with patch.object(p.shutil,'copy2',fail):
   with self.assertRaises(OSError):p.install(self.source,self.target)
  self.assertEqual((self.target/'app/rune.txt').read_text(),'old version')
  self.assertEqual(json.loads((self.target/'release.json').read_text())['version'],'old')
 def test_unknown_nonempty_folder_rejected(self):
  self.target.mkdir();(self.target/'my-file').write_text('keep')
  with self.assertRaises(ValueError):p.install(self.source,self.target)
 def test_overlapping_paths_rejected(self):
  for dest in [self.source,self.source/'sub',self.root]:
   with self.assertRaises(ValueError):p.install(self.source,dest)
 def test_paths(self):
  for name in ['../x','/x','C:/x','runtime/../x','runtime/CON','runtime/a:stream','runtime/x.','runtime//x']:
   with self.subTest(name=name):
    with self.assertRaises(ValueError):p.safe(self.root,name)
 def test_duplicate_manifest(self):
  doc=json.loads((self.source/'files.json').read_text());doc['files'].append(doc['files'][0]);(self.source/'files.json').write_text(json.dumps(doc))
  with self.assertRaises(ValueError):p.verify(self.source)
 def test_changed_file_blocks_rollback(self):
  self.old();p.install(self.source,self.target);(self.target/'app/rune.txt').write_text('user edit')
  with self.assertRaises(ValueError):p.rollback(self.target)
  self.assertEqual((self.target/'app/rune.txt').read_text(),'user edit')
 def test_pack_hash_and_traversal(self):
  self.target.mkdir();archive=self.root/'pack.zip'
  with zipfile.ZipFile(archive,'w') as z:
   z.writestr('pack.json',json.dumps({'format':1,'pack':'test','files':[{'path':'runtime/../../outside','bytes':1,'sha256':'x'}]}));z.writestr('runtime/../../outside','x')
  record={'test':{'bytes':archive.stat().st_size,'sha256':p.sha(archive),'unpackedBytes':1}}
  (self.target/'packs.json').write_text(json.dumps(record))
  with self.assertRaises(ValueError):p.install_pack(self.target,'test',archive)
  self.assertFalse((self.root/'outside').exists())
  archive.write_bytes(b'bad')
  with self.assertRaises(ValueError):p.install_pack(self.target,'test',archive)

 def test_speech_pack_installs_without_changing_preferences(self):
  self.old();archive=self.root/'speech.zip';content=b'model fixture'
  row={'path':'runtime/kokoro/model.bin','bytes':len(content),'sha256':p.hashlib.sha256(content).hexdigest()}
  with zipfile.ZipFile(archive,'w') as z:
   z.writestr('pack.json',json.dumps({'format':1,'pack':'speech-core','files':[row]}));z.writestr(row['path'],content)
  (self.target/'packs.json').write_text(json.dumps({'speech-core':{'bytes':archive.stat().st_size,'sha256':p.sha(archive),'unpackedBytes':len(content)}}))
  p.install_pack(self.target,'speech-core',archive)
  self.assertEqual((self.target/row['path']).read_bytes(),content)
  self.assertEqual((self.target/'bridge/preferences.json').read_text(),'personal settings')
  self.assertEqual(list(self.target.glob('.pack-stage-*')),[])

if __name__=='__main__':unittest.main(verbosity=2)
