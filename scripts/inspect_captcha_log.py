import os
import paramiko
import json
import sys

sys.stdout.reconfigure(encoding='utf-8')

c = paramiko.SSHClient()
c.set_missing_host_key_policy(paramiko.AutoAddPolicy())
c.connect('210.246.215.66', username='root', password=os.environ['FUT_PW'], timeout=10)

node_script = """
const mongoose = require("mongoose");
(async () => {
  await mongoose.connect("mongodb://127.0.0.1:27017/flowultratech");
  const docs = await mongoose.connection.collection("captchalogs").find().sort({ _id: -1 }).limit(3).toArray();
  for (const d of docs) {
    const copy = { ...d };
    for (const k in copy) {
      if (typeof copy[k] === "string" && copy[k].length > 100) {
        copy[k] = copy[k].substring(0, 50) + "... [len=" + copy[k].length + "]";
      }
    }
    console.log(JSON.stringify(copy, null, 2));
  }
  process.exit(0);
})();
"""

cmd = f'cd /root/FlowUltraTech && node -e \'{node_script}\''
_, out, err = c.exec_command(cmd)
res_out = out.read().decode('utf-8')
res_err = err.read().decode('utf-8')
print("STDOUT:\n", res_out)
if res_err:
    print("STDERR:\n", res_err)

c.close()
