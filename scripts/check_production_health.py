import os, sys, json, requests
sys.stdout.reconfigure(encoding='utf-8')
ok=True
def chk(n,c,d=''):
    global ok; ok&=bool(c); print(('PASS' if c else 'FAIL'),n,d)
for host in ('api.flowultratech-captcha.shop','flowultratech-captcha.shop'):
    B='https://'+host
    r=requests.get(B+'/api/v1/app/latest-release',timeout=15); j=r.json()
    chk(host+' latest-release',r.status_code==200 and j['release']['version']=='0.5.0',j['release']['version'])
    r=requests.post(B+'/api/v1/auth/login',json={'username':'root','password':os.environ['FUT_PW'],'hwid':'HEALTH-CHECK'},timeout=20)
    t=r.json().get('accessToken'); chk(host+' login',bool(t),r.status_code)
    r=requests.get(B+'/api/v1/admin/captcha-logs',headers={'Authorization':'Bearer '+(t or '')},timeout=20); chk(host+' admin api',r.status_code==200,r.status_code)
    r=requests.get(B+'/storage/releases/FlowUltraTech_Setup_v0.5.0.exe',stream=True,timeout=30); chk(host+' installer',r.status_code==200 and int(r.headers['content-length'])==60594673); r.close()
    r=requests.get(B+'/socket.io/?EIO=4&transport=polling',timeout=15); chk(host+' socket.io',r.status_code==200 and r.text.startswith('0{'))
    r=requests.get(B+'/admin/',timeout=15); chk(host+' backoffice',r.status_code==200 and 'favicon.png' in r.text)
r=requests.get('http://api.flowultratech-captcha.shop/',allow_redirects=False,timeout=15); chk('http redirect',r.status_code==301)
import socket
s=socket.socket(); s.settimeout(5)
chk('8001 closed publicly',s.connect_ex(('210.246.215.66',8001))!=0)
print('ALL OK' if ok else 'SOME FAILED'); sys.exit(0 if ok else 1)
