import sys
import os
import paramiko
import requests
import json

sys.stdout.reconfigure(encoding='utf-8')

SERVER_HOST = "210.246.215.66"
SERVER_URL = f"http://{SERVER_HOST}:8001"
LOCAL_INSTALLER = r"D:\Script\java-script\FlowUltraTech\storage\releases\FlowUltraTech_Setup_v1.3.0.exe"
REMOTE_INSTALLER = "/root/FlowUltraTech/storage/releases/FlowUltraTech_Setup_v1.3.0.exe"
VERSION = "1.3.0"
MIN_VERSION = "1.3.0" # Force update!
FILE_SIZE = os.path.getsize(LOCAL_INSTALLER)
FILE_HASH = "faa8014dd0a1f625b6546934c4fd6a3b53e5d1b54c2c6d41387a63e431b06361"

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
    "title": f"FlowUltraTech v{VERSION} (Unlimited HWID & Pure Screen Quota Update)",
    "description": (
        "• ปลดล็อกข้อจำกัด HWID (เครื่อง) — ผู้ใช้สามารถเข้าสู่ระบบบนคอมพิวเตอร์กี่เครื่องก็ได้พร้อมกัน ไม่จำกัดจำนวนเครื่อง\n"
        "• ควบคุมและจำกัดเฉพาะจำนวนจอสดสูงสุด (Screen Quota) ที่รันพร้อมกันแบบเรียลไทม์ข้ามทุกอุปกรณ์\n"
        "• ปรับปรุงระบบ Keep-Alive Heartbeat และการตรวจสอบสิทธิ์จอในระดับ Server & Client ให้เสถียรและราบรื่นยิ่งขึ้น\n"
        "• อัปเดตหน้า Backoffice Web UI จัดการโควต้าจอสดและแสดงสถานะเครื่องออนไลน์แบบเรียลไทม์"
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
