# 13. สถานะระบบและปัญหาที่ทราบ (Status & Known Issues)

อัปเดตล่าสุด: 2026-10-20 · เวอร์ชันแอป: 1.7.0

เอกสารนี้แทนที่ `incomplete_and_pending_features.md` และ roadmap เดิม (เก็บไว้ที่ [archive/](archive/)) สรุปจากรายงานการตรวจโค้ดจริงของเอกสาร 01–12 รายละเอียดและลิงก์ไปยังโค้ดอยู่ในเอกสารแต่ละฉบับ

> [!NOTE]
> สถานะที่ระบุว่า "ใช้งานได้" หมายถึงพบโค้ดครบตามเส้นทางทำงาน ไม่ได้หมายความว่ารันทดสอบกับ TikTok จริงแล้ว ข้อที่ยังไม่ยืนยันจากโค้ดมีหัวข้อ "ข้อสังเกต/ช่องว่าง" ท้ายเอกสารแต่ละฉบับ

## 1. สถานะฟีเจอร์

| ระบบ | สถานะ | หมายเหตุ | เอกสาร |
| :--- | :---: | :--- | :--- |
| เปิดหลายอินสแตนซ์ + แยก profile | ✅ | symlink ชื่อโปรเซสต่อโปรไฟล์, ProfileManager อ่าน `services.json` | [02](02_instance_launch_and_window_control.md) |
| ซ่อน/แสดงหน้าต่าง (Win32) | ✅ | ผ่าน `WindowController` | [02](02_instance_launch_and_window_control.md) |
| Native Proxy Bridge | ✅ | HTTP/HTTPS/SOCKS5 + auth เป็นวิธีหลัก | [03](03_network_and_proxy.md) |
| Proxifier | ✅ (ทางเลือก) | รอบสร้างไฟล์ใหม่กับรอบอัปเดตไฟล์เดิมเขียน rule ไม่เหมือนกัน | [03](03_network_and_proxy.md) |
| CDP automation (PuppeteerSharp) | ✅ | selector ส่งแชทอาจเปลี่ยนตามเวอร์ชัน Studio | [05](05_cdp_automation.md) |
| Jigsaw/Slide captcha solver | ✅ | ONNX ใน backend | [06](06_captcha_system.md) |
| Symbol captcha solver | ✅ | Node → Python daemon (FastSAM + DINOv2) ไม่ใช่ ONNX ไม่มี Mock fallback แล้ว | [06](06_captcha_system.md) |
| Inactivity modal | ⚠️ ตรวจจับอย่างเดียว | แจ้งเตือนได้ ไม่มีโค้ดกดปิด modal | [06](06_captcha_system.md), [08](08_live_events_and_auto_reply.md) |
| Live listener (TikTokLive_Sharp) | ✅ | ไม่มี reconnect/proxy/sessionId ในโค้ด listener | [08](08_live_events_and_auto_reply.md) |
| Rule Engine + Keyword Reply | ✅ | | [08](08_live_events_and_auto_reply.md) |
| Auto-Reply Studio 5 โหมด (v1.7.0) | ✅ | รวมวนส่งข้อความ (Broadcast) และ AI Chatbot (OpenRouter) | [08](08_live_events_and_auto_reply.md) |
| Auto Welcome | ⚠️ บางส่วน | กันทักซ้ำด้วย dictionary ในหน่วยความจำ ยังไม่มี LRU/TTL, batch รวมชื่อ, คิว | [08](08_live_events_and_auto_reply.md) |
| Overlay Clock & Anti-Rebroadcast (v1.6.0) | ✅ | พอร์ตเริ่มต้น 19450 | [09](09_overlay_system.md) |
| Telegram / เสียงเตือน | ✅ | Telegram ส่งเฉพาะเหตุการณ์ Captcha | [10](10_notifications_and_alerts.md) |
| Login, slot lease, realtime, force update | ✅ | จำกัดตามจำนวนจอ (`maxTiktokAccounts`) ไม่จำกัดเครื่อง | [11](11_auth_license_and_security.md) |
| VM detection | ❌ ไม่ถูกเรียกใช้ | มีคลาส `VmDetector` และ test แต่ไม่มีจุดเรียกในแอป | [11](11_auth_license_and_security.md) |
| Stream Quality Monitor / Auto Recovery | ❌ ไม่มี | ไม่พบในโค้ด | [10](10_notifications_and_alerts.md) |

## 2. บั๊กและความไม่สอดคล้องในโค้ด

| # | ปัญหา | ที่ที่พบ |
| :-: | :--- | :--- |
| 1 | ตัวนับเพชรใน UI น่าจะนับเกิน (`DiamondCount = cost×amount` แล้ว VM บวก `DiamondCount×RepeatCount` อีกรอบ) | [08](08_live_events_and_auto_reply.md) |
| 2 | `LikeMilestone` ใช้ `TotalLikeFollowCount` แทนยอดไลก์จริง | [08](08_live_events_and_auto_reply.md) |
| 3 | event เข้า Overlay ซ้ำ 2 ทาง และการ์ดที่ไม่มีช่องเป้าหมายรับทุก event | [09](09_overlay_system.md) |
| 4 | ไม่พบจุดเรียก start listener / auto-post timer ตอนเปิดแอป และ `IsLiveChatMonitoring` ไม่ถูกตั้งกลับเป็น true หลังหยุด | [08](08_live_events_and_auto_reply.md) |
| 5 | `AutoHideOnLaunch` และ `AutoConnectCdp` มีใน Settings แต่ไม่มีโค้ดอ่านไปใช้ | [04](04_ui_and_user_guide.md) |
| 6 | ไม่มี UI ผูก `ProxyId` (proxy pool → instance) ทำได้ผ่าน `config.json`/cloud เท่านั้น | [03](03_network_and_proxy.md) |
| 7 | `OpenCaptchaTab` ตั้ง tab index 4 แต่แท็บ Captcha ใน XAML คือ index 3 (ยังไม่ได้รันยืนยัน) | [04](04_ui_and_user_guide.md) |
| 8 | `DebugViewModel` ไม่ถูก bind ใน `MainWindow.xaml` | [04](04_ui_and_user_guide.md) |
| 9 | `KillInstance`/`KillAllStudioProcesses` อาจปิด Studio ที่ผู้ใช้เปิดเองด้วย (ปิดตามชื่อโปรเซส) | [02](02_instance_launch_and_window_control.md) |
| 10 | Verify captcha ไม่มี retry และเสี่ยง false-positive เมื่อไม่พบ modal แล้วส่ง `VERIFIED_SUCCESS` | [06](06_captcha_system.md) |
| 11 | `ActiveSlotLease.AuthToken` ไม่อัปเดตหลัง refresh อาจทำให้ HTTP heartbeat fallback ได้ 401 หลัง 24 ชม. (อนุมานจากโค้ด) | [11](11_auth_license_and_security.md) |
| 12 | Backoffice กับ API ไม่ตรงกัน (`activeLeases` vs `leases`, `isReverted` vs `reverted`, สิทธิ์แท็บ captcha) | [backend/operations_and_backoffice.md](file:///d:/Script/java-script/FlowUltraTech/docs/operations_and_backoffice.md) |
| 13 | `installer/setup_script.iss` เป็นสคริปต์เก่า (v0.1.0) ไม่ตรงกับ `setup.iss` | [12](12_build_release_and_testing.md) |
| 14 | npm scripts ของ backend (`test`, `test:simple`, `test:all`) ชี้พอร์ตเก่า | [12](12_build_release_and_testing.md) |
| 15 | ชุดทดสอบ client บางชุดเรียก production จริง ไม่มี `Skip`/`Trait` จึงล้มเมื่อไม่มีเครือข่าย | [12](12_build_release_and_testing.md) |

## 3. ข้อสังเกตด้านความปลอดภัย

> [!WARNING]
> รายการนี้มาจากการอ่านโค้ด ไม่ได้ทดสอบเจาะระบบ ไม่ได้ใส่ค่า secret ใดลงในเอกสาร

**ความเสี่ยงสูง**
- มี credential จริง (SSH ของ production, รหัส root ของแอป, IP) ฝังในไฟล์ที่ track ใน git ทั้ง 2 repo (client: `scripts/` และ test C#, backend: `scripts/`, `tests/`, `simulate_live_user_app.cjs`) ควร rotate แล้วย้ายไป env
- backend เปิด `express.static` ที่ `/storage` ทั้งโฟลเดอร์ รวม `storage/captchas` โดยไม่ต้องล็อกอิน

**ความเสี่ยงปานกลาง**
- ไม่มี token revocation (logout/suspend ไม่ทำให้ access token ใช้ไม่ได้ทันที) และ lease/captcha ไม่ตรวจสถานะผู้ใช้
- socket event `admin:get_online_users` ไม่ตรวจ role
- `resolveImageInput` รับ URL แล้ว fetch เอง (SSRF)
- `?token=` ใน query ถูก log, cookie `flow_token` ไม่มี `Secure`, client เรียก `http://`
- `ROOT_PASSWORD` มีค่า default ในโค้ดและถูกเขียนทับจาก env ทุกครั้งที่ boot
- `AutoReply.AiApiKey`, proxy password รายบอท และ Telegram token ฝั่ง backend เก็บ plaintext; `GET /admin/users` ส่ง settings ของทุกคนให้ admin; AuditLog snapshot มี `passwordHash`
- Bridge ยอมรับ cert ของ HTTPS upstream ทุกใบ; cloud pull แปลง protocol `https` เป็น SOCKS5; proxy password ถูกส่งขึ้น cloud
- client ไม่ตรวจ `fileHash` ของตัวอัปเดต และตัวติดตั้งไม่ได้เซ็นรหัส

**ความเสี่ยงต่ำ-กลาง**
- `POST /security/event` ไม่มี rate limit/whitelist ผู้ใช้ทั่วไปปลอม event เข้าหน้าแอดมินได้
- deploy script ใช้ `AutoAddPolicy` ของ SSH

## 4. งานที่ควรทำต่อ (เรียงตามความสำคัญ)

1. Rotate และย้าย credential ที่ฝังในโค้ด, ปิด/จำกัด `/storage` ที่เปิดสาธารณะ
2. แก้บั๊กที่กระทบผู้ใช้โดยตรง: ตัวนับเพชร, การเริ่ม listener/auto-post, `OpenCaptchaTab`, การปิด Studio ของผู้ใช้
3. เติม Auto Welcome ให้ครบ (LRU/TTL + batch + คิว) และเพิ่ม unit test ให้ `AutoReplyEngineService`
4. Inactivity modal: เพิ่ม action กดปุ่มยืนยัน/ปิด modal
5. Symbol solver: เพิ่ม `requirements.txt`, ระบุวิธีได้น้ำหนัก DINOv2, วัดอัตราผ่านจริงกับ TikTok และยืนยัน thread-safety ของ daemon
6. จัดชุดทดสอบ: ติด `Trait`/`Skip` ให้เทสต์ที่ต่อ production, แก้พอร์ตเก่าใน npm scripts, ลบ `setup_script.iss`
7. แก้ความไม่ตรงกันของ Backoffice กับ API
