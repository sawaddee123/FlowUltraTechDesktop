import sys
import os
import paramiko
import requests
import json

sys.stdout.reconfigure(encoding='utf-8')

SERVER_HOST = "210.246.215.66"
SERVER_URL = f"http://{SERVER_HOST}:8001"
LOCAL_INSTALLER = r"D:\Script\Test\FlowUltraTech\dist\installer\FlowUltraTech_Setup_v1.5.1.exe"
REMOTE_INSTALLER = "/root/FlowUltraTech/storage/releases/FlowUltraTech_Setup_v1.5.1.exe"
VERSION = "1.5.1"
MIN_VERSION = "1.5.1" # Force update!
FILE_SIZE = os.path.getsize(LOCAL_INSTALLER)
FILE_HASH = "ea956517a6dd20f9d22227dc57774f2abdf5c1b39f951fad71ef88bd94f0d326"

print(f"1. Connecting SSH to {SERVER_HOST}...")
c = paramiko.SSHClient()
c.set_missing_host_key_policy(paramiko.AutoAddPolicy())
c.connect(SERVER_HOST, username='root', password='!WFXU#v1aO7338+', timeout=10)

sftp = c.open_sftp()
try:
    sftp.mkdir("/root/FlowUltraTech/storage/releases")
except Exception:
    pass

print(f"2. Uploading {LOCAL_INSTALLER} ({FILE_SIZE / (1024*1024):.2f} MB) -> {REMOTE_INSTALLER}...")
def progress_callback(transferred, total):
    percent = (transferred / total) * 100
    if transferred % (5 * 1024 * 1024) < 65536 or transferred == total:
        print(f"   Progress: {transferred / (1024*1024):.1f} / {total / (1024*1024):.1f} MB ({percent:.1f}%)")

sftp.put(LOCAL_INSTALLER, REMOTE_INSTALLER, callback=progress_callback)
sftp.close()
c.close()
print("   Installer upload complete!")

print("3. Logging in as root on Central Server...")
login_res = requests.post(f"{SERVER_URL}/api/v1/auth/login", json={
    "username": "root",
    "password": "!WFXU#v1aO7338+",
    "hwid": "RELEASE-BROADCAST-SCRIPT"
}).json()

token = login_res.get("accessToken")
if not token:
    print("Login failed:", login_res)
    sys.exit(1)

print("   Logged in successfully as root.")

print(f"4. Broadcasting new release with Force Update (isMandatory=True, minRequiredVersion={MIN_VERSION})...")
headers = {
    "Authorization": f"Bearer {token}",
    "Content-Type": "application/json"
}

broadcast_payload = {
    "version": VERSION,
    "minRequiredVersion": MIN_VERSION,
    "title": f"FlowUltraTech v{VERSION} (Autonomous 24/7 AI Captcha & Auto-Refresh Token)",
    "description": (
        "• รองรับการรันบอทอัตโนมัติระยะยาว 24/7 แบบ Unattended ไม่ต้องเฝ้าหน้าจอ\n"
        "• เพิ่มระบบ Proactive Auto-Renewal ตรวจและต่ออายุ Token อัตโนมัติล่วงหน้าก่อนส่งภาพแก้ Captcha\n"
        "• เพิ่มระบบ Reactive On-Demand Retry ขอ Token ใหม่ทันทีและส่งซ้ำหากพบ 401 Unauthorized\n"
        "• แก้ไข Token Desync ใน Background Watchdog และซิงค์ Token ทุกช่องทางอัตโนมัติ"
    ),
    "fileName": f"FlowUltraTech_Setup_v{VERSION}.exe",
    "fileSize": FILE_SIZE,
    "fileHash": FILE_HASH,
    "downloadUrl": f"/storage/releases/FlowUltraTech_Setup_v{VERSION}.exe",
    "isMandatory": True
}

broadcast_res = requests.post(f"{SERVER_URL}/api/v1/admin/releases/broadcast", headers=headers, json=broadcast_payload)
print(f"   Broadcast HTTP Status: {broadcast_res.status_code}")
print(f"   Response:\n{json.dumps(broadcast_res.json(), indent=2, ensure_ascii=False)}")

print("5. Verifying latest release endpoint...")
latest_res = requests.get(f"{SERVER_URL}/api/v1/app/latest-release").json()
print(f"   Latest Release from Server:\n{json.dumps(latest_res, indent=2, ensure_ascii=False)}")
print("\nAll operations finished successfully!")
