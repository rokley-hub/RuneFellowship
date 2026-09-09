"""Regression checks for download integrity, interruption, archive safety and user-data retention."""
import hashlib,io,json,shutil,unittest,uuid,zipfile
from pathlib import Path
from unittest.mock import patch
import online_installer as app
import package_tools as packages

class Response(io.BytesIO):
    def __init__(self,data,status=200,headers=None):super().__init__(data);self.status=status;self.headers=headers or {}
class Opener:
    def __init__(self,handler):self.handler=handler;self.calls=[]
    def open(self,req,timeout):self.calls.append(req);return self.handler(req)
class Tests(unittest.TestCase):
    def setUp(self):
        self.base=Path(__file__).resolve().parent/'.online-tests';self.base.mkdir(exist_ok=True)
        self.root=self.base/uuid.uuid4().hex;self.root.mkdir()
        self.data=b'Rune verified bytes';self.row=dict(name='fixture',url='https://publisher.example/file',sha256=hashlib.sha256(self.data).hexdigest(),bytes=len(self.data),kind='file',target='runtime/model.bin',group='speech')
    def tearDown(self):shutil.rmtree(packages.safe(self.base,self.root.name))
    def test_resume(self):
        (self.root/(self.row['sha256']+'.part')).write_bytes(self.data[:5])
        opener=Opener(lambda req:Response(self.data[5:],206,{'Content-Range':'bytes 5-18/19'}))
        self.assertEqual(app.download(self.row,self.root,opener=opener).read_bytes(),self.data)
        self.assertEqual(opener.calls[0].get_header('Range'),'bytes=5-')
    def test_server_ignores_range_restarts(self):
        (self.root/(self.row['sha256']+'.part')).write_bytes(b'Rune ')
        self.assertEqual(app.download(self.row,self.root,opener=Opener(lambda _:Response(self.data))).read_bytes(),self.data)
    def test_wrong_hash_never_activated(self):
        with self.assertRaises(RuntimeError):app.download(self.row,self.root,opener=Opener(lambda _:Response(b'x'*len(self.data))))
        self.assertFalse((self.root/self.row['sha256']).exists())
    def test_invalid_range_rejected(self):
        (self.root/(self.row['sha256']+'.part')).write_bytes(b'Rune ')
        with self.assertRaises(RuntimeError):app.download(self.row,self.root,opener=Opener(lambda _:Response(self.data[5:],206,{'Content-Range':'bytes 6-18/19'})))
    def test_verified_cache_no_network(self):
        (self.root/self.row['sha256']).write_bytes(self.data)
        opener=Opener(lambda _:self.fail('Cache must avoid a request'))
        app.download(self.row,self.root,opener=opener);self.assertEqual(opener.calls,[])
    def test_cancel_keeps_partial(self):
        part=self.root/(self.row['sha256']+'.part');part.write_bytes(b'Rune ')
        cancel=self.root/'cancel';cancel.touch()
        with self.assertRaises(app.Cancelled):app.download(self.row,self.root,cancel,Opener(lambda _:self.fail()))
        self.assertTrue(part.exists())
    def test_path_traversal(self):
        file=self.root/'bad.zip'
        with zipfile.ZipFile(file,'w') as z:z.writestr('../outside.txt','bad')
        with self.assertRaises(ValueError):app.unpack(file,dict(kind='zip',target='runtime'),self.root/'stage')
        self.assertFalse((self.root/'stage/outside.txt').exists())
    def test_wheel_data_and_notices(self):
        file=self.root/'sample.whl'
        with zipfile.ZipFile(file,'w') as z:
            z.writestr('sample.data/purelib/sample.py','value=1');z.writestr('sample.dist-info/licenses/LICENSE','licence')
        app.unpack(file,dict(kind='wheel',target='runtime/packages'),self.root/'stage')
        self.assertTrue((self.root/'stage/runtime/packages/sample.py').is_file())
        self.assertEqual((self.root/'stage/runtime/packages/sample.dist-info/licenses/LICENSE').read_text(),'licence')
    def test_failed_download_does_not_change_installation(self):
        source=self.root/'source';core=source/'Rune';core.mkdir(parents=True)
        (core/'release.json').write_text('{"version":"new"}')
        (core/'files.json').write_text(json.dumps({'format':1,'files':[{'path':'release.json','bytes':(core/'release.json').stat().st_size,'sha256':packages.sha(core/'release.json')}]}))
        (source/'online-downloads.json').write_text(json.dumps({'downloads':[self.row]}))
        dest=self.root/'installed';dest.mkdir();(dest/'release.json').write_text('old');(dest/'preferences.json').write_text('keep')
        with patch.object(app,'download',side_effect=RuntimeError('offline')):
            with self.assertRaises(RuntimeError):app.install(source,dest,self.root/'cache',['speech'])
        self.assertEqual((dest/'release.json').read_text(),'old');self.assertEqual((dest/'preferences.json').read_text(),'keep')
    def test_insecure_source_rejected(self):
        with self.assertRaises(ValueError):app.download(dict(self.row,url='http://publisher.example/file'),self.root)
if __name__=='__main__':unittest.main()
