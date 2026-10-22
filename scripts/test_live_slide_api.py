import os
import urllib.request
import json
import re

# 1. Login as root
login_data = json.dumps({'username': 'root', 'password': os.environ['FUT_PW'], 'hwid': 'test'}).encode()
req = urllib.request.Request('https://api.flowultratech-captcha.shop/api/v1/auth/login', data=login_data, headers={'Content-Type': 'application/json'})
res = urllib.request.urlopen(req)
token = json.loads(res.read().decode())['accessToken']
print('Login OK!')

# 2. Read real TikTok captcha image from extracted sources
with open(r'D:\Script\Test\FlowUltraTech\extracted_sources\CaptchaJigsaw.html', 'r', encoding='utf-8') as f:
    html = f.read()

m = re.search(r'id="captcha-verify-image"\s+src="data:image/webp;base64,([^"]+)"', html)
if not m:
    print('Error: Captcha image not found')
    exit(1)

base64_img = m.group(1)
payload = json.dumps({
    'image': base64_img,
    'displayWidth': 288,
    'displayHeight': 179
}).encode()

req2 = urllib.request.Request(
    'https://api.flowultratech-captcha.shop/api/v1/captcha/slide',
    data=payload,
    headers={
        'Content-Type': 'application/json',
        'Authorization': 'Bearer ' + token
    }
)

res2 = urllib.request.urlopen(req2)
data2 = json.loads(res2.read().decode())
print('Slide API response from 210.246.215.66:')
print(json.dumps(data2, indent=2))
