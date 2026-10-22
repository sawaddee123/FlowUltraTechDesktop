import os
import urllib.request
import json

login_data = json.dumps({'username': 'root', 'password': os.environ['FUT_PW'], 'hwid': 'test'}).encode()
req = urllib.request.Request('https://api.flowultratech-captcha.shop/api/v1/auth/login', data=login_data, headers={'Content-Type': 'application/json'})
res = urllib.request.urlopen(req)
token = json.loads(res.read().decode())['accessToken']
print('Login OK, token length:', len(token))

req2 = urllib.request.Request('https://api.flowultratech-captcha.shop/api/v1/admin/captcha-logs', headers={'Authorization': 'Bearer ' + token})
res2 = urllib.request.urlopen(req2)
data2 = json.loads(res2.read().decode())
print('Total logs:', data2['total'])
for l in data2['logs']:
    print(f"ID: {l.get('captchaId')}, type: {l.get('captchaType')}, status: {l.get('status')}, user: {l.get('username')}, time: {l.get('createdAt')}")
