"""Rune package verification, transactional install and conservative rollback."""
from pathlib import Path
import argparse, hashlib, json, os, shutil, tempfile, zipfile, sys, uuid

def sha(p):
    with p.open('rb') as f: return hashlib.file_digest(f,'sha256').hexdigest()

def safe(root, relative):
    relative=relative.replace('\\','/')
    parts=relative.split('/')
    if not relative or any(p in ('','.', '..') or ':' in p or p.endswith((' ','.')) for p in parts): raise ValueError('Unsafe package path')
    # Windows reserved names cannot be written as normal files.
    if any(p.split('.')[0].upper() in {'CON','PRN','AUX','NUL',*[f'COM{i}' for i in range(1,10)],*[f'LPT{i}' for i in range(1,10)]} for p in parts): raise ValueError('Reserved package path')
    target=root.joinpath(*parts)
    if not target.resolve().is_relative_to(root.resolve()): raise ValueError('Package escapes its folder')
    for ancestor in [target,*target.parents]:
        if ancestor == root.parent: break
        if ancestor.is_symlink() or (hasattr(ancestor,'is_junction') and ancestor.is_junction()): raise ValueError('Linked installation folders are unsupported')
    return target

def read_manifest(root):
    doc=json.loads((root/'files.json').read_text())
    if doc.get('format')!=1 or len(doc['files'])>100000: raise ValueError('Unsupported release manifest')
    seen=set()
    for f in doc['files']:
        p=safe(root,f['path']); key=str(p).lower()
        if key in seen: raise ValueError('Duplicate package path')
        seen.add(key)
    return doc

def verify(root):
    manifest=read_manifest(root)
    for f in manifest['files']:
        p=safe(root,f['path'])
        if not p.is_file() or p.stat().st_size!=f['bytes'] or sha(p)!=f['sha256']: raise ValueError('Damaged or missing file: '+f['path'])
    return manifest

def install(source,target):
    source=source.resolve(); target=target.resolve()
    if target==source or target.is_relative_to(source) or source.is_relative_to(target): raise ValueError('Install in a separate folder outside the download folder')
    manifest=verify(source)
    if target.exists() and any(target.iterdir()) and not (target/'release.json').is_file(): raise ValueError('Choose an empty folder or an existing Rune installation')
    target.mkdir(parents=True,exist_ok=True)
    size=sum(f['bytes'] for f in manifest['files'])
    if shutil.disk_usage(target).free < size*2+100_000_000: raise ValueError('Not enough free space for install and rollback')
    backup=target/'release-backups'/uuid.uuid4().hex; backup.mkdir(parents=True)
    transaction=[]
    entries=manifest['files']+[{'path':'files.json','sha256':sha(source/'files.json')}]
    try:
        for i,f in enumerate(entries):
            dest=safe(target,f['path']); src=safe(source,f['path'])
            old=dest.exists(); previous=sha(dest) if old else None
            row={'path':f['path'],'existed':old,'installed':f['sha256'],'previous':previous}
            if old:
                b=safe(backup,f['path']);b.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(dest,b)
            transaction.append(row)
            dest.parent.mkdir(parents=True,exist_ok=True)
            temp=dest.with_name(dest.name+'.rune-install-'+uuid.uuid4().hex)
            try: shutil.copy2(src,temp);os.replace(temp,dest)
            finally:
                if temp.exists(): temp.unlink()
            if i%250==0: print(f'Installing {i+1}/{len(entries)}',flush=True)
        (backup/'transaction.json').write_text(json.dumps(transaction,indent=2))
        (target/'last-release-backup.txt').write_text(backup.name)
        print('Installed. Your bridge and mod profiles were not changed.',flush=True)
    except Exception:
        restore(target,backup,transaction,check=False)
        raise

def restore(target,backup,rows,check=True):
    for f in rows:
        p=safe(target,f['path'])
        if check and p.exists() and sha(p)!=f['installed']: raise ValueError('File changed since install; rollback stopped: '+f['path'])
        if f['existed'] and (not safe(backup,f['path']).is_file() or sha(safe(backup,f['path']))!=f['previous']): raise ValueError('Rollback backup is damaged')
    for f in reversed(rows):
        p=safe(target,f['path'])
        if f['existed']: shutil.copy2(safe(backup,f['path']),p)
        elif p.exists(): p.unlink()

def rollback(target):
    name=(target/'last-release-backup.txt').read_text().strip()
    if len(name)!=32 or any(c not in '0123456789abcdef' for c in name): raise ValueError('Invalid rollback record')
    backup=safe(target,'release-backups/'+name)
    restore(target,backup,json.loads((backup/'transaction.json').read_text()))
    (target/'last-release-backup.txt').unlink()
    print('Previous application files restored; user data retained.')

def install_pack(root,identifier,archive):
    catalog=json.loads((root/'packs.json').read_text()); expected=catalog[identifier]
    if archive.stat().st_size!=expected['bytes'] or sha(archive)!=expected['sha256']: raise ValueError('Pack failed integrity verification')
    if shutil.disk_usage(root).free<expected['unpackedBytes']*2+100_000_000: raise ValueError('Not enough space for this pack')
    with tempfile.TemporaryDirectory(prefix='.pack-stage-',dir=root) as staging:
        stage=Path(staging)
        with zipfile.ZipFile(archive) as z:
            if z.getinfo('pack.json').file_size>30_000_000: raise ValueError('Pack manifest too large')
            manifest=json.loads(z.read('pack.json')); seen=set()
            if manifest.get('pack')!=identifier or manifest.get('format')!=1: raise ValueError('Wrong pack')
            if len(manifest['files'])>100000: raise ValueError('Too many pack files')
            for f in manifest['files']:
                relative=f['path'];key=relative.lower()
                if key in seen or not relative.startswith('runtime/'): raise ValueError('Invalid pack member')
                seen.add(key);dest=safe(stage,relative); safe(root,relative)
                info=z.getinfo(relative)
                if info.file_size!=f['bytes']: raise ValueError('Invalid pack size')
                dest.parent.mkdir(parents=True,exist_ok=True)
                with z.open(info) as src,dest.open('wb') as out: shutil.copyfileobj(src,out,1024*1024)
                if sha(dest)!=f['sha256']: raise ValueError('Pack member hash failed')
            if sum(f['bytes'] for f in manifest['files'])!=expected['unpackedBytes']: raise ValueError('Pack size mismatch')
        # All unpacking and verification completes before any installed file changes.
        backup=stage/'backup'; applied=[]
        try:
            for f in manifest['files']:
                dest=safe(root,f['path']);old=dest.exists()
                if old:
                    b=safe(backup,f['path']);b.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(dest,b)
                applied.append((f['path'],old));dest.parent.mkdir(parents=True,exist_ok=True);os.replace(safe(stage,f['path']),dest)
        except Exception:
            for relative,old in reversed(applied):
                dest=safe(root,relative)
                if old: shutil.copy2(safe(backup,relative),dest)
                elif dest.exists(): dest.unlink()
            raise
    print('Installed '+identifier,flush=True)

def uninstall(root):
    manifest=read_manifest(root)
    kept=[]
    for f in manifest['files']:
        p=safe(root,f['path'])
        if p.is_file():
            if sha(p)==f['sha256']: p.unlink()
            else: kept.append(f['path'])
    print('Removed unchanged core application files. Profiles, memories, optional packs, backups and modified files remain. No game saves were touched.')
    if kept: print('Retained modified files: '+', '.join(kept))

if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('action',choices=['verify','install','rollback','pack']);parser.add_argument('root',type=Path);parser.add_argument('extra',nargs='*');a=parser.parse_args()
    try:
        if a.action=='verify': print(json.dumps({'ok':True,'release':verify(a.root)['release']}))
        elif a.action=='install': install(a.root,Path(a.extra[0]))
        elif a.action=='rollback': rollback(a.root)
        elif a.action=='pack': install_pack(a.root,a.extra[0],Path(a.extra[1]))
    except Exception as e: print(str(e),file=sys.stderr);sys.exit(1)
