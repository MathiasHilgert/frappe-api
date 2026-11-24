import subprocess,sys
# Rebase todo for folding a chain's fixups. A fixup moves under its slice only when no commit it would jump over touched one of its
# files; otherwise it stays where it was, as its own `fix(...)` commit. Every commit is amended once so the commit-msg hook strips trailers.
def git(*a): return subprocess.run(['git',*a],capture_output=True,text=True).stdout
commits=[l.split(' ',1) for l in git('log','--reverse','--format=%H %s','origin/main..HEAD').strip().split('\n')]
files={h:set(git('show','--name-only','--format=',h).split()) for h,_ in commits}
target={}
for i,(h,s) in enumerate(commits):
    if s.startswith('fixup! '):
        target[h]=next(j for j in range(i-1,-1,-1) if commits[j][1]==s[7:])
keep=set()
changed=True
while changed:
    changed=False
    for i,(h,s) in enumerate(commits):
        if h not in target or h in keep: continue
        t=target[h]
        between=[commits[j][0] for j in range(t+1,i) if commits[j][0] not in target or commits[j][0] in keep or target[commits[j][0]]!=t]
        if any(files[b] & files[h] for b in between): keep.add(h); changed=True
todo=[]; strip='exec git commit -q --amend --no-edit'
for i,(h,s) in enumerate(commits):
    if h in target and h not in keep: continue
    todo.append(f'pick {h} {s}')
    if h in keep:
        subj=s[7:]; scope=subj.split(':',1)[0].split('(')[-1].rstrip(')')
        msg=f'fix({scope}): follow up on {subj.split(": ",1)[1]}'[:100]
        todo.append(f'exec git commit -q --amend -m "{msg}"')
    else:
        for f,t in target.items():
            if t==i and f not in keep: todo.append(f'fixup {f}')
        todo.append(strip)
open(sys.argv[1],'w').write('\n'.join(todo)+'\n')
print('kept as own commits:', [commits[[c[0] for c in commits].index(k)][1][:70] for k in keep], file=sys.stderr)
