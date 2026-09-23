# TikTok LIVE Studio Multi-Instance & Automation Sandbox

ระบบและสถาปัตยกรรมสำหรับรัน **TikTok LIVE Studio แบบหลายบัญชี (Multi-Instance)**, **แยก Proxy 100% รายโปรแกรม**, **ควบคุมการทำงานผ่าน CDP (Chrome DevTools Protocol)**, **สั่งส่งข้อความสด (Send Live Message)** และ **ลดการใช้ทรัพยากรเครื่องให้ต่ำที่สุด (Pseudo-Headless Mode)** เพื่อรองรับข้อกำหนดของ TikTok ยุคใหม่ที่ต้องสตรีมผ่านตัวโปรแกรมทางการเท่านั้น

---

## สารบัญเอกสารและสถาปัตยกรรม (Architecture Modules)

เอกสารใน Sandbox นี้ถูกแบ่งออกเป็น 6 ส่วนหลักเพื่อให้เข้าใจหลักการและนำไปพัฒนาต่อได้ทันที:

| เอกสาร | หัวข้อหลัก | รายละเอียดสังเขป |
| :--- | :--- | :--- |
| [01. Multi-Instance Architecture](file:///D:/Script/Test/FlowUltraTech/docs/01_multi_instance_architecture.md) | การเปิดหลายโปรแกรมพร้อมกัน | กลไก Mutex และการแยก User Data Directory (Direct Profile Isolation) |
| [02. Network & Proxy Isolation](file:///D:/Script/Test/FlowUltraTech/docs/02_network_and_proxy_isolation.md) | การแยกเครือข่ายและ Proxy รายจอ | การ Route ทราฟฟิก (Web + RTMP/UDP) ผ่าน Proxifier ป้องกันการโดนแบนกลุ่ม |
| [03. CDP & WebView Automation](file:///D:/Script/Test/FlowUltraTech/docs/03_cdp_webview_automation.md) | การควบคุมและส่งข้อความเข้า WebView | การเปิด Remote Debugging Port, ส่งแชท (Send Message), ดักจับ WebSocket/API |
| [04. Resource Optimization & Headless](file:///D:/Script/Test/FlowUltraTech/docs/04_resource_optimization_headless.md) | การรีดประสิทธิภาพและทำกึ่ง Headless | การซ่อนหน้าต่าง (`SW_HIDE`), ลดภาระ GPU Compositor, และข้อจำกัด NVENC |
| [05. Related Tools & Ecosystem](file:///D:/Script/Test/FlowUltraTech/docs/05_related_tools_and_ecosystem.md) | เครื่องมือที่เกี่ยวข้องและ Git Repos | รายชื่อ Library, Open-Source Tools, GitHub Repositories ที่เกี่ยวข้อง |
| [06. TikTok-Live-Connector Guide](file:///D:/Script/Test/FlowUltraTech/docs/06_tiktok_live_connector_guide.md) | การเชื่อมต่อ Webcast ดักจับแชต/ของขวัญ | Reverse-engineered Webcast, Event Listener, การต่อ Proxy/SessionId, สถาปัตยกรรมไฮบริดร่วมกับ CDP |
| [07. Captcha Mock Testing Guide](file:///D:/Script/Test/FlowUltraTech/docs/07_captcha_mock_testing_guide.md) | คู่มือทดสอบระบบแก้ CAPTCHA ด้วย Mock | การตรวจจับ DOM, Jigsaw Slider, Symbol Click, และการจำลองด้วย Mock Server |
| [08. Full Features & Architecture Roadmap](file:///D:/Script/Test/FlowUltraTech/docs/08_full_features_and_architecture_roadmap.md) | แผนผังและเทคนิคการพัฒนาระบบครบ 9 ฟีเจอร์ | สถาปัตยกรรม Hybrid, AI Solver, Telegram Alert, Auto Welcome, OpenRouter AI, และ Stream Quality |
| [09. MongoDB Schema & Central Auth Architecture](file:///D:/Script/Test/FlowUltraTech/docs/09_mongodb_schema_and_auth_architecture.md) | สถาปัตยกรรม MongoDB และระบบ Login กลาง | การออกแบบ Schema ผู้ใช้, Device Limit (HWID), เซสชัน TTL, โปรไฟล์ และ Captcha Quota |
| [10. Anti-Circumvention & Account Enforcement](file:///D:/Script/Test/FlowUltraTech/docs/10_anti_circumvention_and_account_enforcement.md) | ระบบป้องกันการเปิดเกินโควต้าและโกงบัญชี | ป้องกัน Multi-Process, Deep HWID, Dynamic Lease & Heartbeat, Captcha Guard, VM Detection |

---

## แผนภาพภาพรวมระบบ (Overall System Flow)

```mermaid
graph TD
    subgraph Central Control
        Bot["Central Bot / WPF Controller & Automation"]
    end

    subgraph Network Isolation Layer (Proxifier)
        P1["Proxy SOCKS5 - IP #1"]
        P2["Proxy SOCKS5 - IP #2"]
    end

    subgraph Instance Isolation (Direct Profile Mode)
        subgraph Instance 1 [Acc01 - Profile Data]
            S1["TikTok LIVE Studio (Hidden/No UI)"]
            CDP1["CDP Port :9221"]
            WV1["Chromium WebView (Chat & Controls)"]
            OBS1["OBS Engine Core (Media Stream)"]
        end

        subgraph Instance 2 [Acc02 - Profile Data]
            S2["TikTok LIVE Studio (Hidden/No UI)"]
            CDP2["CDP Port :9222"]
            WV2["Chromium WebView (Chat & Controls)"]
            OBS2["OBS Engine Core (Media Stream)"]
        end
    end

    Bot -->|Control & Send Message via CDP| CDP1
    Bot -->|Control & Send Message via CDP| CDP2

    CDP1 --> WV1
    CDP2 --> WV2

    S1 --> P1
    S2 --> P2

    P1 -->|Encrypted Stream & Auth| TikTokServers["TikTok Servers"]
    P2 -->|Encrypted Stream & Auth| TikTokServers
```

---

## ฟังก์ชันการทำงานหลัก (Core Features)

1. **Direct Profile Launcher:** เปิด TikTok LIVE Studio หลายบัญชีพร้อมกันโดยแยกโฟลเดอร์ `--user-data-dir` อิสระ ไม่ต้องลงโปรแกรมเสริมภายนอก
2. **Profile Fetcher & Visual Badges:** ตรวจสอบและดึงข้อมูลโปรไฟล์ผู้ใช้ (Avatar, TikTok Handle `@displayId`, Nickname, UID) จาก `services.json` มาแสดงบนการ์ดอินสแตนซ์อัตโนมัติ
3. **Ultra-Light Background Login:** โหมดล็อกอินประหยัดพลังงานระดับสูง (0% GPU, ซ่อนหน้าต่างเบื้องหลัง) รองรับ Callback จากบราวเซอร์ และปิดโปรเซสอัตโนมัติเมื่อยืนยันตัวตนสำเร็จ
4. **CDP Client & WebView Automation:** ควบคุมและส่งข้อความเข้าห้องไลฟ์อัตโนมัติผ่าน Chrome DevTools Protocol
5. **Window Optimization:** ซ่อน/แสดงหน้าต่าง UI ผ่าน Win32 API (`SW_HIDE`) เพื่อลดภาระ GPU Compositor
6. **Proxifier Routing:** กำหนดเส้นทางเครือข่ายและเชื่อมต่อ Proxy ราย Instance เพื่อความปลอดภัยสูงสุด
