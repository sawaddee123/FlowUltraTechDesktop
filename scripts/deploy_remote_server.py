import sys
import paramiko
import os

sys.stdout.reconfigure(encoding='utf-8')
c = paramiko.SSHClient()
c.set_missing_host_key_policy(paramiko.AutoAddPolicy())
c.connect('210.246.215.66', username='root', password='!WFXU#v1aO7338+', timeout=10)

sftp = c.open_sftp()

local_base = r"d:\Script\java-script\FlowUltraTech"
remote_base = "/root/FlowUltraTech"

files_to_sync = [
    "src/routes/auth.ts",
    "src/routes/slots.ts",
    "src/models/User.ts",
    "src/controllers/adminController.ts",
    "src/services/socketHub.ts",
]

for rel_path in files_to_sync:
    local_path = os.path.join(local_base, rel_path.replace("/", "\\"))
    remote_path = f"{remote_base}/{rel_path}"
    print(f"Uploading {local_path} -> {remote_path}...")
    sftp.put(local_path, remote_path)

# Upload backoffice dist files
local_backoffice_dist = r"d:\Script\java-script\backoffice\dist"
remote_backoffice_dist = "/root/FlowUltraTech/backoffice/dist"

def upload_dir(local_dir, remote_dir):
    try:
        sftp.mkdir(remote_dir)
    except Exception:
        pass
    for item in os.listdir(local_dir):
        l_item = os.path.join(local_dir, item)
        r_item = f"{remote_dir}/{item}"
        if os.path.isdir(l_item):
            upload_dir(l_item, r_item)
        else:
            sftp.put(l_item, r_item)

print(f"Syncing backoffice dist -> {remote_backoffice_dist}...")
upload_dir(local_backoffice_dist, remote_backoffice_dist)
print("Backoffice dist sync completed.")

sftp.close()

# Rebuild on remote and restart PM2
print("Building and restarting PM2 on remote server...")
build_cmd = f"cd {remote_base} && npm run build && pm2 restart flowultratech-backend"
_, out, err = c.exec_command(build_cmd)
print("OUT:\n", out.read().decode('utf-8', errors='replace'))
print("ERR:\n", err.read().decode('utf-8', errors='replace'))

c.close()
print("Remote update finished successfully!")
