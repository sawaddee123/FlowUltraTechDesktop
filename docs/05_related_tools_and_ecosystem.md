# 05. Related Tools & Ecosystem (เครื่องมือที่เกี่ยวข้องและ Git Repos)

รวบรวมโปรเจกต์โอเพนซอร์ส ซอฟต์แวร์ และไลบรารีที่เกี่ยวข้องกับการบริดจ์ ควบคุม และทำงานร่วมกับ TikTok LIVE Studio และ Webcast

---

## 1. โปรเจกต์ควบคุมภายในและการสั่งการสตูดิโอ (Studio Control)

* **[dannguyen9x/tiktok-live-studio-mcp](https://github.com/dannguyen9x/tiktok-live-studio-mcp)**
  * **ภาษา/เทคโนโลยี:** TypeScript, Node.js, Model Context Protocol (MCP)
  * **ลักษณะงาน:** เชื่อมต่อพอร์ต Socket.IO ภายในของ TikTok LIVE Studio (พอร์ตที่ใช้กับ Elgato Stream Deck) เพื่อเปิดเป็น MCP Tool ให้ AI หรือระบบภายนอกสั่งเปลี่ยนฉาก ปิด/เปิดเลเยอร์ และปรับเสียง

---

## 2. ไลบรารีดักจับแชตและอีเวนต์แบบเรียลไทม์ (Webcast & Event Connectors)

* **[zerodytrash/TikTok-Live-Connector](https://github.com/zerodytrash/TikTok-Live-Connector)**
  * **ภาษา/เทคโนโลยี:** Node.js (JavaScript / TypeScript), Protobuf, WebSocket
  * **ลักษณะงาน:** ไลบรารี Reverse-engineered ระดับมาตรฐานสำหรับเชื่อมต่อ TikTok Webcast Push Service ดักจับข้อความแชต (`chat`), ของขวัญและ Streak (`gift`), การเคาะไลก์ (`like`), สถิติผู้ชมสด (`roomUser`), และผู้เข้าห้อง (`member`) โดยไม่ต้องใช้รหัสผ่าน (ใช้เพียง `@username`)
  * **บทบาทในโปรเจกต์นี้:** ใช้เป็น **Event Listener / Ingestion Layer** แยกต่างหากเพื่อลดการใช้ทรัพยากร แทนที่จะต้องคอยรันเบราว์เซอร์หรือสแครปหน้าจอ สามารถผสานกับ CDP Automation สำหรับส่งข้อความตอบกลับได้
  * **เอกสารและคู่มือเชิงลึกในโปรเจกต์:** ดูรายละเอียด การติดตั้ง ตัวเลือก Proxy และตัวอย่างโค้ดฉบับเต็มได้ที่ [06. TikTok-Live-Connector Integration Guide](file:///D:/Script/Test/TiktokStudioSandbox/docs/06_tiktok_live_connector_guide.md)
* **[isaackogan/TikTokLive](https://github.com/isaackogan/TikTokLive)**
  * **ภาษา/เทคโนโลยี:** Python, AsyncIO, Protobuf
  * **ลักษณะงาน:** พอร์ตภาษา Python ของ TikTok-Live-Connector เหมาะมากสำหรับนำมาทำ Data Pipeline, Bot ตอบแชท, หรือบันทึกสถิติลงฐานข้อมูล

---

## 3. ซอฟต์แวร์จัดการสภาพแวดล้อมและ Proxy (Environment & Network)

* **[Proxifier](https://www.proxifier.com/)**
  * **ลักษณะงาน:** ซอฟต์แวร์ระดับองค์กรสำหรับดักทราฟฟิกเครือข่ายทุกชนิด (TCP, UDP, RTMP) บังคับให้ออกผ่าน Proxy SOCKS5/HTTPS ป้องกันการรั่วไหลของ IP Address
* **[Sysinternals Handle](https://learn.microsoft.com/en-us/sysinternals/downloads/handle)**
  * **ลักษณะงาน:** ยูทิลิตี้จาก Microsoft สำหรับค้นหาและสั่งปิด Mutex Handle ในหน่วยความจำโดยไม่ต้องปิดโปรแกรม

---

## 4. เครื่องมือระบบภาพและสตรีม (OBS & Media Tools)

* **[obs-ndi](https://github.com/obs-ndi/obs-ndi)**
  * **ลักษณะงาน:** ปลั๊กอินส่งสัญญาณภาพและเสียงผ่านเครือข่ายท้องถิ่นความหน่วงต่ำ สำหรับเชื่อมต่อ OBS เข้ากับ TikTok LIVE Studio แบบไม่ต้องใช้สายฮาร์ดแวร์
* **[keylase/nvidia-patch](https://github.com/keylase/nvidia-patch)**
  * **ลักษณะงาน:** แพตช์สำหรับปลดล็อกข้อจำกัดจำนวน Concurrent NVENC Encoding Sessions บนการ์ดจอ NVIDIA GeForce ช่วยให้สามารถสตรีมพร้อมกันได้มากกว่า 5 จอบนการ์ดเดียว
