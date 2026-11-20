import subprocess,re,sys
def show(rev,path):
    return subprocess.run(['git','show',f'{rev}:{path}'],capture_output=True).stdout.decode('utf-8','replace')
def ls(rev,prefix):
    return subprocess.run(['git','ls-tree','-r','--name-only',rev,'--',prefix],capture_output=True,text=True).stdout.split()
P='src/PvPSolver/PvPSolver/'
def enums(rev):
    t=show(rev,P+'Basic/Data/RSCommandType.cs'); out={}
    for m in re.finditer(r'public enum (\w+)[^{]*\{(.*?)\n\}',t,re.S):
        body=re.sub(r'/\*.*?\*/|//[^\n]*','',m.group(2),flags=re.S)
        body=re.sub(r'\[[^\]]*\]','',body)
        out[m.group(1)]=[x.strip().split('=')[0].strip() for x in body.split(',') if x.strip()]
    return out
def cases(rev):
    s=set()
    for f in ls(rev,P+'Commands'):
        t=show(rev,f)
        for m in re.finditer(r'case\s+([\w\.]+)\s*:',t): s.add(m.group(1))
    return s
a,b=enums('940ac875f'),enums('HEAD')
for k in a:
    print(k,len(a[k]),'->',len(b.get(k,[])),'lost:',[x for x in a[k] if x not in b.get(k,[])],'added:',[x for x in b.get(k,[]) if x not in a[k]])
ca,cb=cases('940ac875f'),cases('HEAD')
print('case labels lost:',sorted(ca-cb)); print('case labels added:',sorted(cb-ca)); print(len(ca),len(cb))
