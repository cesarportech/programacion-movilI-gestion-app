"""Synthetic end-to-end checks. --seed writes SQL for the authorized Supabase tool.
No service key is used by this client. Test credentials are generated per run.
"""
import base64, hashlib, json, pathlib, secrets, sys, urllib.request, urllib.error, uuid

state_path = pathlib.Path(sys.argv[2])
BASE = 'https://efekzvmgnicztweluyim.supabase.co/functions/v1/fredi-api/'
def call(route, data, token=None):
    headers = {'Content-Type': 'application/json'}
    if token: headers['Authorization'] = 'Bearer ' + token
    req = urllib.request.Request(BASE + route, None if data is None else json.dumps(data).encode(), headers)
    try:
        with urllib.request.urlopen(req, timeout=45) as r: return r.status, json.load(r)
    except urllib.error.HTTPError as e: return e.code, json.load(e)
def check(condition, text):
    if not condition: raise AssertionError(text)
    print('PASS remote ' + text)
def record(table, data):
    return {'Table':table, 'Key':str(data.get('Id', data.get('Key', data.get('CompanyId')))), 'Data':data}
def upload(dataset, scope, revision, records):
    return {'Dataset':dataset, 'Scope':scope, 'RequestId':str(uuid.uuid4()), 'BaseRevision':revision, 'Records':records}

if sys.argv[1] == '--seed':
    dataset, password = str(uuid.uuid4()), secrets.token_urlsafe(24)
    users = []
    for i, name in enumerate(['master','alpha','bravo'], 1):
        salt = secrets.token_bytes(16)
        users.append({'Id':i, 'Username':name, 'Disabled':False, 'SecurityVersion':0, 'IsMaster':i==1, 'CompanyId':max(0,i-1),
          'Salt':base64.b64encode(salt).decode(), 'PasswordHash':base64.b64encode(hashlib.pbkdf2_hmac('sha256', password.encode(), salt, 210000, 32)).decode()})
    state_path.write_text(json.dumps({'dataset':dataset,'password':password,'users':users}))
    sql = "insert into public.fredi_users(dataset,id,username,payload) values ('%s',1,'master','%s'::jsonb);" % (dataset, json.dumps(users[0]))
    state_path.with_suffix('.sql').write_text(sql)
    print('Synthetic seed prepared.')
else:
    state = json.loads(state_path.read_text()); d,p,users = state['dataset'],state['password'],state['users']
    code, result = call('v1/auth/login', {'dataset':d,'username':'master','password':p})
    check(code==200 and 'token' in result, 'custom password authentication'); master = result['token']
    code,_ = call('v1/auth/login', {'dataset':d,'username':'master','password':'wrong'})
    check(code==401, 'wrong password rejected')
    admin = [record('LocalUser',u) for u in users] + [record('Company',{'Id':i,'Name':'Test '+str(i),'CatalogPackageId':0}) for i in [1,2]]
    body = upload(d,'admin',0,admin)
    code,result = call('v1/sync',body,master); check(code==200 and result.get('revision')==1,'master uploads companies and users')
    _,result = call('v1/auth/login',{'dataset':d,'username':'alpha','password':p}); alpha=result['token']
    accounts = [record('Account',{'Id':1,'CompanyId':1,'Code':'1','Description':'Synthetic'})]
    body = upload(d,'company:1',0,accounts)
    code,result = call('v1/sync',body,alpha); check(code==200 and result.get('revision')==1,'operator uploads own company')
    code,result = call('v1/sync',body,alpha); check(code==200 and result.get('revision')==1,'lost-response retry has no duplicate revision')
    changed = json.loads(json.dumps(body)); changed['Records'][0]['Data']['Description']='tampered retry'
    code,_ = call('v1/sync',changed,alpha); check(code==409,'request ID cannot be reused with different content')
    code,_ = call('v1/sync',upload(d,'company:1',0,accounts),alpha); check(code==409,'stale installation cannot overwrite newer data')
    code,_ = call('v1/sync',upload(d,'company:2',0,[]),alpha); check(code==403,'cross-company write rejected')
    code,_ = call('v1/sync',upload(d,'admin',1,admin),alpha); check(code==403,'operator cannot promote itself or replace users')
    code,_ = call('v1/sync',upload(d,'company:1',1,[])); check(code==401,'anonymous upload rejected')
    accounts[0]['Data']['Description']='Edited'
    code,result = call('v1/sync',upload(d,'company:1',1,accounts),alpha); check(code==200 and result.get('revision')==2,'edit advances revision')
    code,result = call('v1/sync',upload(d,'company:1',2,[]),alpha); check(code==200 and result.get('revision')==3,'delete advances revision without stale rows')
    code,result = call('v1/contabilidades/1',None,alpha); check(code==200 and result.get('records')==[] and result.get('revision')==3,'authorized read verifies deleted records are absent')
    code,_ = call('v1/contabilidades/2',None,alpha); check(code==403,'cross-company read rejected')
    code,result = call('v1/contabilidades',None,alpha); check(code==200 and len(result.get('companies',[]))==1,'company list is filtered by user')
    code,_ = call('v1/catalogos',None,alpha); check(code==403,'catalog library remains master-only')
    users[1]['Disabled']=True; users[1]['SecurityVersion']=1
    admin = [record('LocalUser',u) for u in users] + [record('Company',{'Id':i,'Name':'Test '+str(i),'CatalogPackageId':0}) for i in [1,2]]
    code,_ = call('v1/sync',upload(d,'admin',1,admin),master); check(code==200,'master publishes access revocation')
    code,_ = call('v1/status',{},alpha); check(code==401,'already-open cloud session revoked immediately')
    code,_ = call('v1/auth/login',{'dataset':d,'username':'alpha','password':p}); check(code==401,'suspended user cannot authenticate')
    code,_ = call('v1/auth/logout',{},master); check(code==200,'cloud logout')
    code,_ = call('v1/status',{},master); check(code==401,'token rejected after logout')
    print('Remote synthetic checks complete. Dataset: '+d)
