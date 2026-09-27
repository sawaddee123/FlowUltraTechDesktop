import paramiko

c = paramiko.SSHClient()
c.set_missing_host_key_policy(paramiko.AutoAddPolicy())
c.connect('210.246.215.66', username='root', password='!WFXU#v1aO7338+', timeout=10)

node_code = """
const fs = require('fs');
const { findPositionDetails } = require('./dist/ml/slide');
(async () => {
  const buf = fs.readFileSync('storage/captchas/1790490107292_1000000_slide_b2490283.webp');
  const res = await findPositionDetails(buf);
  console.log('Result:', JSON.stringify(res, null, 2));
  process.exit(0);
})();
"""

sftp = c.open_sftp()
with sftp.file('/root/FlowUltraTech/temp_test.cjs', 'w') as f:
    f.write(node_code)
sftp.close()

_, out, err = c.exec_command('cd /root/FlowUltraTech && node temp_test.cjs && rm -f temp_test.cjs')
print(out.read().decode('utf-8'))
print(err.read().decode('utf-8'))
c.close()
