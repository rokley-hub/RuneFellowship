"""Install Rune with locked, original-source downloads. Python standard library only."""
import argparse, hashlib, json, os, shutil, sys, tarfile, time, urllib.error, urllib.request, uuid, zipfile
from pathlib import Path
import package_tools as packages

class Cancelled(Exception):pass
def emit(message):print(message,flush=True)
def check_cancel(cancel):
    if cancel and cancel.exists():raise Cancelled('Cancelled. Verified downloads are kept for your next attempt.')
def fingerprint(row):
    for algorithm in ['sha256','sha512']:
        value=row.get(algorithm,'')
        if len(value)==hashlib.new(algorithm).digest_size*2 and all(c in '0123456789abcdef' for c in value):return algorithm,value
    raise ValueError('Missing download checksum')
def matches(path,row):
    if not path.is_file() or (row.get('bytes') and path.stat().st_size!=row['bytes']):return False
    algorithm,digest=fingerprint(row)
    with path.open('rb') as f:return hashlib.file_digest(f,algorithm).hexdigest()==digest
class HTTPSRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,req,fp,code,msg,headers,newurl):
        if not newurl.startswith('https://'):raise ValueError('Refused an insecure download redirect')
        return super().redirect_request(req,fp,code,msg,headers,newurl)
def download(row,cache,cancel=None,opener=None):
    algorithm,digest=fingerprint(row);cache.mkdir(parents=True,exist_ok=True)
    final=packages.safe(cache,digest);partial=packages.safe(cache,digest+'.part')
    if matches(final,row):return final
    if not row['url'].startswith('https://') and opener is None:raise ValueError('Downloads require HTTPS')
    opener=opener or urllib.request.build_opener(HTTPSRedirect())
    for attempt in range(3):
        check_cancel(cancel)
        if partial.exists() and row.get('bytes') and partial.stat().st_size>=row['bytes']:
            if matches(partial,row):os.replace(partial,final);return final
            partial.unlink()
        offset=partial.stat().st_size if partial.exists() else 0
        headers={'User-Agent':'RuneFellowship-Setup/0.4.26','Accept-Encoding':'identity'}
        if offset:headers['Range']='bytes='+str(offset)+'-'
        try:
            with opener.open(urllib.request.Request(row['url'],headers=headers),timeout=30) as response:
                append=response.status==206 and offset>0
                if response.status==206 and not response.headers.get('Content-Range','').startswith('bytes '+str(offset)+'-'):raise ValueError('Unexpected resume range')
                if not append:offset=0
                total=row.get('bytes') or (offset+int(response.headers.get('Content-Length','0')))
                last=0
                with partial.open('ab' if append else 'wb') as out:
                    while True:
                        check_cancel(cancel);block=response.read(1024*1024)
                        if not block:break
                        out.write(block);offset+=len(block)
                        if total and offset>total:raise ValueError('Download exceeds its expected size')
                        if time.monotonic()-last>1:
                            emit(row['name']+' · '+str(round(offset/1e6))+' / '+str(round(total/1e6))+' MB');last=time.monotonic()
            if not matches(partial,row):
                partial.unlink();raise ValueError('File checksum does not match the tested release: '+row['name'])
            os.replace(partial,final);return final
        except (OSError,urllib.error.URLError,ValueError) as error:
            if attempt==2:raise RuntimeError('Could not download '+row['name']+'. '+str(error)) from error
            emit('Retrying '+row['name']+' ('+str(attempt+2)+'/3)');check_cancel(cancel)
    raise RuntimeError('Download failed')
def unpack(archive,row,stage,cancel=None):
    root=packages.safe(stage,row['target']);kind=row['kind']
    if kind=='file':root.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(archive,root);return
    root.mkdir(parents=True,exist_ok=True)
    if kind=='pure-source':
        # One reviewed pure-Python dependency has no publisher wheel. Never run setup.py.
        with tarfile.open(archive,'r:gz') as src:
            for item in src:
                check_cancel(cancel)
                if not item.isfile():
                    if item.issym() or item.islnk():raise ValueError('Linked source archive member')
                    continue
                if item.name.startswith(row['prefix']):relative=item.name[len(row['prefix']):]
                elif item.name.rsplit('/',1)[-1] in ['LICENSE','PKG-INFO','README.txt']:
                    relative='antlr4_upstream_notices/'+item.name.rsplit('/',1)[-1]
                else:continue
                dest=packages.safe(root,relative);dest.parent.mkdir(parents=True,exist_ok=True)
                with src.extractfile(item) as inp,dest.open('wb') as out:shutil.copyfileobj(inp,out)
        return
    seen=set()
    with zipfile.ZipFile(archive) as src:
        if sum(x.file_size for x in src.infolist())>40_000_000_000:raise ValueError('Archive expands beyond the installer limit')
        for item in src.infolist():
            check_cancel(cancel)
            if item.is_dir():continue
            if (item.external_attr>>16)&0o170000==0o120000:raise ValueError('Linked archive member')
            relative=item.filename
            packages.safe(root,relative)
            if kind=='wheel' and '.data/' in relative:
                _,sub=relative.split('.data/',1);scheme,relative=sub.split('/',1)
                if scheme not in ['purelib','platlib','data']:
                    # CLI entry points are not used by Rune. Retain these files as upstream notices.
                    relative='_wheel_aux/'+item.filename
            dest=packages.safe(root,relative);key=str(dest).lower()
            if key in seen:raise ValueError('Duplicate archive member')
            seen.add(key);dest.parent.mkdir(parents=True,exist_ok=True)
            with src.open(item) as inp,dest.open('wb') as out:shutil.copyfileobj(inp,out,1024*1024)
    if row['target']=='runtime/python':(root/'python312._pth').write_text('python312.zip\n.\n..\nimport site\n')
def install(source,destination,cache,groups,cancel=None):
    source=source.resolve();destination=destination.resolve();cache=cache.resolve()
    if destination==source or destination.is_relative_to(source) or source.is_relative_to(destination):raise ValueError('Choose an install folder outside the extracted installer')
    if destination.exists() and any(destination.iterdir()) and not (destination/'release.json').is_file():raise ValueError('Choose an empty folder or an existing Rune installation')
    if cache==destination or cache.is_relative_to(destination) or destination.is_relative_to(cache):raise ValueError('Keep the download cache separate from the install folder')
    groups=set(groups)|{'core'}
    if groups-{'core','speech','brain','turbo','v3'}:raise ValueError('Unknown model selection')
    if groups&{'turbo','v3'}:groups|={'speech','expressive'}
    doc=json.loads((source/'online-downloads.json').read_text());rows=[r for r in doc['downloads'] if r['group'] in groups]
    if doc.get('errors'):raise ValueError('The download list is incomplete')
    for row in rows:fingerprint(row);packages.safe(Path('validation'),row['target'])
    packages.verify(source/'Rune')
    cache.mkdir(parents=True,exist_ok=True)
    required=sum(r.get('bytes',0) for r in rows)
    if shutil.disk_usage(cache).free<required*3+300_000_000:raise ValueError('Not enough free space to download, unpack and verify these choices')
    # Stage and validate everything before modifying the existing installation.
    with packages.pack_staging(cache) as staging:
        stage=Path(staging)/'Rune';shutil.copytree(source/'Rune',stage)
        for index,row in enumerate(rows):
            check_cancel(cancel);emit('Download '+str(index+1)+'/'+str(len(rows))+' · '+row['name'])
            file=download(row,cache,cancel);emit('Preparing '+row['name']);unpack(file,row,stage,cancel)
        receipt={'format':1,'groups':sorted(groups),'downloads':rows}
        (stage/'online-installed.json').write_text(json.dumps(receipt,indent=2))
        (stage/'files.json').write_text(json.dumps({'format':1,'release':json.loads((stage/'release.json').read_text())['version'],'files':[{'path':p.relative_to(stage).as_posix(),'bytes':p.stat().st_size,'sha256':packages.sha(p)} for p in sorted(stage.rglob('*')) if p.is_file() and p.name!='files.json']},indent=2))
        check_cancel(cancel);emit('Finishing installation. Please keep setup open.');packages.install(stage,destination)
    emit('Ready. Open Rune.exe in your installed folder.')
def main():
    p=argparse.ArgumentParser();p.add_argument('--source',type=Path,required=True);p.add_argument('--destination',type=Path,required=True);p.add_argument('--cache',type=Path,required=True);p.add_argument('--groups',default='speech');p.add_argument('--cancel',type=Path);a=p.parse_args()
    try:install(a.source,a.destination,a.cache,filter(None,a.groups.split(',')),a.cancel)
    except Exception as e:emit(str(e));return 1
    return 0
if __name__=='__main__':sys.exit(main())
