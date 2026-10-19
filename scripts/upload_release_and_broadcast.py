import sys
import os
import hashlib
import paramiko
import requests
import json

sys.stdout.reconfigure(encoding='utf-8')

VERSION = "1.6.0"
MIN_VERSION = "1.6.0" # Force update!
SERVER_HOST = "210.246.215.66"
SERVER_URL = f"http://{SERVER_HOST}:8001"
ROOT_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LOCAL_INSTALLER = os.path.join(ROOT_DIR, "dist", "installer", f"FlowUltraTech_Setup_v{VERSION}.exe")
REMOTE_INSTALLER = f"/root/FlowUltraTech/storage/releases/FlowUltraTech_Setup_v{VERSION}.exe"

if not os.path.exists(LOCAL_INSTALLER):
    print(f"ERROR: Installer not found at {LOCAL_INSTALLER}")
    sys.exit(1)

FILE_SIZE = os.path.getsize(LOCAL_INSTALLER)
sha256 = hashlib.sha256()
with open(LOCAL_INSTALLER, "rb") as f:
    for chunk in iter(lambda: f.read(1024 * 1024), b""):
        sha256.update(chunk)
FILE_HASH = sha256.hexdigest()

print(f"0. Prepared {LOCAL_INSTALLER}")
print(f"   Size: {FILE_SIZE:,} bytes ({FILE_SIZE / (1024*1024):.2f} MB)")
print(f"   SHA256: {FILE_HASH}")

print(f"1. Connecting SSH to {SERVER_HOST}...")
c = paramiko.SSHClient()
c.set_missing_host_key_policy(paramiko.AutoAddPolicy())
c.connect(SERVER_HOST, username='root', password='!WFXU#v1aO7338+', timeout=15)

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
    "title": f"FlowUltraTech v{VERSION} (Real-time Clock & Anti-Rebroadcast Live Proof Overlay)",
    "description": (
        "• เพิ่มระบบ Real-time Clock & Live Proof Overlay ซิงก์เวลาจริงผ่าน Local WebSocket Hot-Reload เปลี่ยนดีไซน์สดโดยไม่ต้องวางลิงก์ใหม่ พร้อมพรีวิวสด WebView2 ในแอป\n"
        "• รองรับหน้าปัดนาฬิกา 6 สไตล์ พร้อมเอฟเฟกต์ Camera Physics (แสงสะท้อนกระจก, เงาตกกระทบ, กล้องขยับระดับพิกเซล)\n"
        "• เพิ่มป้ายวิ่ง Interactive Live Proof Ticker แสดงผู้ชม/ของขวัญ/แชทสด คู่กับป้ายนับถอยหลังโปรนาทีทอง (Flash Deal Countdown)\n"
        "• เพิ่มปุ่ม One-Click CDP Auto-Inject สั่งฝังลิงก์นาฬิกาเข้าฉาก TikTok LIVE Studio อัตโนมัติในคลิกเดียว"
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
