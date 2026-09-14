import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const PORT = parseInt(process.env.PORT || process.argv.find(a => a.startsWith('--port='))?.split('=')[1] || '8899', 10);

const jigsawFile = path.join(__dirname, 'mock_captcha_jigsaw.html');
const symbolFile = path.join(__dirname, 'mock_captcha_symbol.html');

const indexHtml = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>TikTok LIVE Studio - Captcha Mock Testbed</title>
  <style>
    body {
      background: #0f1015;
      color: #fff;
      font-family: system-ui, -apple-system, sans-serif;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      min-height: 100vh;
      margin: 0;
      padding: 20px;
    }
    .container {
      max-width: 680px;
      width: 100%;
      background: #181920;
      border: 1px solid rgba(255,255,255,0.1);
      border-radius: 12px;
      padding: 32px;
      box-shadow: 0 10px 30px rgba(0,0,0,0.5);
    }
    h1 { font-size: 24px; color: #fe2c55; margin-bottom: 8px; display: flex; align-items: center; gap: 10px; }
    p { color: rgba(255,255,255,0.7); font-size: 14px; line-height: 1.6; }
    .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin: 24px 0; }
    .card {
      background: #23242c;
      border: 1px solid rgba(255,255,255,0.1);
      border-radius: 8px;
      padding: 20px;
      text-decoration: none;
      color: inherit;
      transition: transform 0.15s, border-color 0.15s;
      display: flex;
      flex-direction: column;
    }
    .card:hover { transform: translateY(-2px); border-color: #fe2c55; }
    .card-badge {
      display: inline-block;
      align-self: flex-start;
      font-size: 11px;
      font-weight: 700;
      padding: 3px 8px;
      border-radius: 4px;
      margin-bottom: 12px;
    }
    .badge-jigsaw { background: rgba(254, 44, 85, 0.2); color: #fe2c55; }
    .badge-symbol { background: rgba(32, 213, 236, 0.2); color: #20d5ec; }
    .card-title { font-size: 17px; font-weight: 600; margin-bottom: 6px; }
    .card-desc { font-size: 12px; color: rgba(255,255,255,0.6); flex: 1; margin-bottom: 14px; }
    .card-btn {
      display: inline-block;
      text-align: center;
      background: #fe2c55;
      color: #fff;
      padding: 8px 14px;
      border-radius: 6px;
      font-weight: 600;
      font-size: 12px;
    }
    .cdp-guide {
      background: #121318;
      border: 1px solid rgba(255,255,255,0.06);
      border-radius: 8px;
      padding: 16px;
      font-size: 13px;
      margin-top: 20px;
    }
    .cdp-guide code {
      display: block;
      background: #090a0d;
      color: #20d5ec;
      padding: 10px;
      border-radius: 4px;
      margin-top: 8px;
      font-family: Consolas, monospace;
      font-size: 12px;
      overflow-x: auto;
    }
  </style>
</head>
<body>
  <div class="container">
    <h1>🧩 TikTok Studio Captcha Mocks</h1>
    <p>จำลองหน้าต่าง Inactivity Captcha ของ TikTok LIVE Studio อิงตามข้อมูลโครงสร้าง DOM และคลาสของจริงใน Captcha_Jigsaw.txt และ Captcha_Symbol.txt</p>

    <div class="grid">
      <a href="/jigsaw" class="card" target="_blank">
        <span class="card-badge badge-jigsaw">SLIDE CAPTCHA</span>
        <div class="card-title">Captcha Jigsaw</div>
        <div class="card-desc">สไลด์เลื่อนจิ๊กซอว์เข้าช่องว่าง พร้อมภาพทดสอบจริงและคลาส .secsdk-captcha-drag-icon</div>
        <span class="card-btn">เปิดหน้า Jigsaw ↗</span>
      </a>

      <a href="/symbol" class="card" target="_blank">
        <span class="card-badge badge-symbol">SYMBOL MATCHING</span>
        <div class="card-title">Captcha Symbol</div>
        <div class="card-desc">คลิกเลือกสัญลักษณ์เหมือนกัน 2 จุด พร้อมปักหมุดพิกัด และปุ่มกดยืนยัน Confirm</div>
        <span class="card-btn">เปิดหน้า Symbol ↗</span>
      </a>
    </div>

    <div class="cdp-guide">
      <b>🚀 ตัวอย่างคำสั่งเปิด Chrome พร้อมพอร์ต CDP สำหรับทดสอบกับ TiktokStudioSandbox:</b>
      <code>chrome.exe --remote-debugging-port=9221 http://localhost:${PORT}/jigsaw</code>
    </div>
  </div>
</body>
</html>`;

const server = http.createServer((req, res) => {
  const url = req.url || '/';

  if (url === '/' || url === '/index.html') {
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
    res.end(indexHtml);
  } else if (url === '/jigsaw' || url === '/mock_captcha_jigsaw.html') {
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
    fs.createReadStream(jigsawFile).pipe(res);
  } else if (url === '/symbol' || url === '/mock_captcha_symbol.html') {
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
    fs.createReadStream(symbolFile).pipe(res);
  } else {
    res.writeHead(404, { 'Content-Type': 'text/plain' });
    res.end('Not Found');
  }
});

server.listen(PORT, () => {
  console.log(`\n======================================================`);
  console.log(`🚀 TikTok Studio Captcha Mock Server running at:`);
  console.log(`   ➜ Dashboard: http://localhost:${PORT}/`);
  console.log(`   ➜ Jigsaw:    http://localhost:${PORT}/jigsaw`);
  console.log(`   ➜ Symbol:    http://localhost:${PORT}/symbol`);
  console.log(`======================================================\n`);
});
