import sys
import paramiko

sys.stdout.reconfigure(encoding='utf-8')
c = paramiko.SSHClient()
c.set_missing_host_key_policy(paramiko.AutoAddPolicy())
c.connect('210.246.215.66', username='root', password='!WFXU#v1aO7338+', timeout=10)

cmd = "mongosh flowultratech --eval 'print(\"COUNT:\", db.captchalogs.countDocuments()); print(JSON.stringify(db.captchalogs.find({}, {imageBase64:0}).sort({_id:-1}).limit(2).toArray(), null, 2))'"
_, out, err = c.exec_command(cmd)
print("OUT:\n", out.read().decode('utf-8', errors='replace'))
print("ERR:\n", err.read().decode('utf-8', errors='replace'))
c.close()
