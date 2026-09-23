import json, time, urllib.request, urllib.parse, sys, os
BASE='https://v2.xivapi.com/api/sheet'
SCHEMA='exdschema@2:latest'
OUT=os.path.dirname(os.path.abspath(__file__))
def get(url):
    for i in range(4):
        try:
            req=urllib.request.Request(url, headers={'User-Agent':'crucible-research/1.0'})
            with urllib.request.urlopen(req, timeout=60) as r:
                return json.load(r)
        except urllib.error.HTTPError as e:
            if e.code==404: return None
            body=e.read()[:300]
            print('HTTP',e.code,url,body,file=sys.stderr); time.sleep(2*(i+1))
        except Exception as e:
            print('ERR',e,url,file=sys.stderr); time.sleep(2*(i+1))
    raise SystemExit('failed '+url)
def row(sheet, rid, sub=None, fields=None):
    q={'schema':SCHEMA}
    if fields: q['fields']=fields
    rs=f'{rid}' if sub is None else f'{rid}:{sub}'
    time.sleep(0.25)
    return get(f'{BASE}/{sheet}/{rs}?'+urllib.parse.urlencode(q, safe='@(),*'))
def rows(sheet, fields, limit=500, rowids=None):
    out=[]; after=None
    while True:
        q={'schema':SCHEMA,'limit':limit,'fields':fields}
        if rowids is not None: q['rows']=','.join(map(str,rowids))
        if after is not None: q['after']=after
        time.sleep(0.3)
        d=get(f'{BASE}/{sheet}?'+urllib.parse.urlencode(q, safe='@(),*:'))
        rs=d.get('rows',[])
        out.extend(rs)
        if rowids is not None or len(rs)<limit: break
        last=rs[-1]
        after=f"{last['row_id']}" if 'subrow_id' not in last else f"{last['row_id']}:{last['subrow_id']}"
    return out
