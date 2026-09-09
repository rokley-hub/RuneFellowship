"""Export an allowlisted source tree. Personal deny terms are supplied locally, never embedded."""
from pathlib import Path
import argparse, hashlib, json, re, shutil, struct, zipfile

def main():
 parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,required=True);parser.add_argument('--deny',action='append',default=[]);a=parser.parse_args()
 project=Path(__file__).resolve().parent.parent;out=a.output.resolve()
 if out.exists():raise RuntimeError('Use a new empty export directory')
 out.mkdir(parents=True)
 excluded={'bin','obj','__pycache__','.git','.codex','.agents'}
 allowed={'.cs','.csproj','.xaml','.png','.py','.ps1','.md','.json','.txt','.config','.blueprint'}
 entries=[]
 for folder in ['src','tests','audio','blueprint-library']:
  for p in (project/folder).rglob('*'):
   rel=p.relative_to(project)
   if not p.is_file() or excluded.intersection(rel.parts) or p.suffix.lower() not in allowed:continue
   if p.is_symlink() or not p.resolve().is_relative_to(project):raise RuntimeError('Source path escapes project')
   entries.append((p,rel))
 for name in ['create-blueprints.py','check-blueprints.py']:entries.append((project/'tools'/name,Path('tools')/name))
 # Only authored packaging sources, not the staged app, downloaded archives or test data.
 for p in (project/'release').iterdir():
  if p.is_file() and p.suffix in {'.py','.ps1','.md'}:entries.append((p,Path('packaging')/p.name))
 for p in (project/'release/docs').glob('*.md'):entries.append((p,Path('docs')/p.name))
 entries += [(project/'NuGet.Config',Path('NuGet.Config')),(project/'Launcher.cs',Path('Launcher.cs'))]
 for p in (project/'release/public').iterdir():
  if p.is_file():entries.append((p,Path(p.name)))
 for src,rel in entries:
  target=out/rel;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(src,target)
 report=audit(out,a.deny)
 (out/'SOURCE-PRIVACY-REPORT.json').write_text(json.dumps(report,indent=2))
 if not report['passed']:raise RuntimeError('Privacy scan failed; review local report before distribution')
 archive=out.with_name(out.name+'.zip')
 with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
  for p in sorted(out.rglob('*')):
   if p.is_file():z.write(p,p.relative_to(out))
 print(json.dumps({'files':report['files'],'passed':True,'archive':archive.name,'sha256':hashlib.sha256(archive.read_bytes()).hexdigest()}))

def audit(root,deny):
 findings=[];count=0;images=0
 allowed_email='rokley@gmail.com'
 for p in sorted(root.rglob('*')):
  if not p.is_file():continue
  count+=1;rel=p.relative_to(root).as_posix();data=p.read_bytes()
  if any(term.lower() in rel.lower() for term in deny):findings.append({'file':rel,'reason':'Personal deny term in filename'})
  if p.suffix=='.png':
   images+=1
   if data[:8]!=b'\x89PNG\r\n\x1a\n':findings.append({'file':rel,'reason':'Invalid PNG'});continue
   pos=8
   while pos+12<=len(data):
    length=struct.unpack('>I',data[pos:pos+4])[0];kind=data[pos+4:pos+8]
    if kind in [b'tEXt',b'zTXt',b'iTXt',b'eXIf']:findings.append({'file':rel,'reason':'Image metadata requires review'})
    pos+=12+length
   continue
  text=data.decode('utf-8-sig',errors='replace')
  if any(term.lower() in text.lower() for term in deny):findings.append({'file':rel,'reason':'Personal deny term'})
  if re.search(r'(?i)[A-Z]:[/\\]Users[/\\](?!Public\b|Default\b)[^\s"<>]+|/(?:home|Users)/[A-Za-z0-9_.-]+/',text):findings.append({'file':rel,'reason':'Absolute user-home path'})
  emails=set(re.findall(r'[\w.+%-]+@[\w.-]+\.[A-Za-z]{2,}',text))
  if emails-{allowed_email}:findings.append({'file':rel,'reason':'Unexpected email address'})
  if re.search(r'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----|\bsk-[A-Za-z0-9_-]{24,}|\bgh[pousr]_[A-Za-z0-9]{25,}',text):findings.append({'file':rel,'reason':'Credential-shaped content'})
 return {'passed':not findings,'files':count,'pngFilesCheckedForTextOrExif':images,'findings':findings,'scope':'Allowlisted source, tests, generated UI assets and new public documentation. No git history, credentials, conversations, user profiles, logs or binary build outputs exported.','limitation':'Pattern and metadata inspection is not a mathematical proof that no sensitive data exists; manually review before publishing.'}

if __name__=='__main__':main()
