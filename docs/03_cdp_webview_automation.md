# 03. CDP & WebView Automation (การควบคุมและส่งข้อความเข้า WebView)

## 1. หลักการทำงานของ Chrome DevTools Protocol (CDP)

UI และระบบแชตของ TikTok LIVE Studio ทำงานอยู่บน Chromium ทำให้นักพัฒนาสามารถเปิดช่องทางดีบักและสั่งการจากระยะไกลได้ผ่านพารามิเตอร์:

```bash
"TikTok LIVE Studio.exe" --remote-debugging-port=9221
```

เมื่อเปิดพอร์ตนี้ ตัว Chromium ภายในจะเปิด REST API และ WebSocket Server ไว้ที่ `http://localhost:9221` ทำหน้าที่เป็นสะพานเชื่อมให้เราส่งคำสั่ง JavaScript, สั่งคลิก, สั่งพิมพ์ข้อความ, หรือดักจับทราฟฟิกได้

---

## 2. การเชื่อมต่อและสั่งการส่งข้อความสด (Send Live Message)

### ตัวอย่างสคริปต์ Python ด้วย Playwright:
```python
import asyncio
from playwright.async_api import async_playwright

async def send_live_chat(port: int, message: str):
    async with async_playwright() as p:
        # เชื่อมต่อเข้าไปยัง TikTok LIVE Studio ผ่านพอร์ต CDP
        browser = await p.chromium.connect_over_cdp(f"http://localhost:{port}")
        
        # ค้นหา WebView/Page ที่เป็นส่วนของห้องแชท
        chat_page = None
        for context in browser.contexts:
            for page in context.pages:
                if "chat" in page.url.lower() or "webcast" in page.url.lower():
                    chat_page = page
                    break
        
        if chat_page:
            # 1. พิมพ์ข้อความลงในช่องแชท
            await chat_page.locator("textarea[placeholder*='chat']").fill(message)
            # 2. กดปุ่มส่ง หรือกด Enter
            await chat_page.keyboard.press("Enter")
            print(f"[{port}] ส่งข้อความสำเร็จ: {message}")
        else:
            print(f"[{port}] ไม่พบหน้าต่างแชต")

asyncio.run(send_live_chat(9221, "สวัสดีครับทุกคน ยินดีต้อนรับสู่ไลฟ์!"))
```

---

## 3. การดักจับ API & WebSocket (In-Context Network Hooking)

แทนที่จะอ่านข้อความจากหน้าจอ (DOM Scraping) ซึ่งอาจช้าหรือไม่แม่นยำ เราสามารถใช้ CDP ดักจับอีเวนต์เครือข่ายได้โดยตรง:

```python
# การดักจับ WebSocket Frame ผ่าน CDP
cdp_session = await page.context.new_cdp_session(page)
await cdp_session.send("Network.enable")

async def on_ws_frame(params):
    payload = params.get("response", {}).get("payloadData")
    # ดักจับข้อมูลแชต, ของขวัญ, หรือสถิติห้องไลฟ์แบบเรียลไทม์
    print("WebSocket Received:", payload)

cdp_session.on("Network.webSocketFrameReceived", on_ws_frame)
```

---

## 4. การยิงส่งข้อความตรงผ่าน In-Context Fetch (ไม่ต้องคลิก UI)

เราสามารถใช้ `page.evaluate()` สั่งให้เบราว์เซอร์ส่ง HTTP POST ไปยัง Endpoint แชตของ TikTok โดยตรง ซึ่งจะใช้ Session, Cookie, และ Token ของบัญชีนั้นที่ล็อกอินอยู่แล้วในตัวโปรแกรม 100%:

```javascript
await chat_page.evaluate("""
    fetch('/webcast/room/chat/', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ content: 'ข้อความอัตโนมัติ' })
    })
""");
```

---

## 5. การดึงและส่งออกโครงสร้างหน้าเว็บทั้งหมด (Export Full HTML / DOM Dump)

สำหรับการวิเคราะห์ DOM, หา CSS Selectors, หรือตรวจสอบสถานะ UI ของ TikTok LIVE Studio สามารถดึง Full HTML ผ่าน Puppeteer/CDP หรือกดปุ่ม **"🌐 Export Full HTML"** บนแอปพลิเคชัน:

```csharp
// ตัวอย่างการดึง Full HTML ผ่าน PuppeteerSharp:
var html = await page.GetContentAsync();
// หรือดึงผ่าน EvaluateExpression:
var outerHtml = await page.EvaluateExpressionAsync<string>("document.documentElement.outerHTML");
```
ระบบจะเพิ่ม Comment Header กำกับ URL, Title, วันเวลา และพอร์ตของบัญชีนั้น แล้วบันทึกเป็นไฟล์ `.html` ที่สามารถเปิดดูใน Browser หรือ Text Editor ได้ทันที
