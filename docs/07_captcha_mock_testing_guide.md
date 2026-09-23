# 07. คู่มือการทดสอบ Captcha จำลอง (Captcha Mock Testing Guide)

เอกสารนี้อธิบายการใช้งานระบบจำลอง (Mock) ของ Inactivity Captcha ทั้ง 2 รูปแบบ ได้แก่ **Jigsaw (Slide Captcha)** และ **Symbol (เลือกสัญลักษณ์ที่เหมือนกัน)** โดยสร้างขึ้นจากโค้ด HTML/DOM ดัมพ์ของจริง 100% จาก TikTok LIVE Studio:
- [Captcha_Jigsaw.txt](file:///d:/Script/Test/FlowUltraTech/docs/Captcha_Jigsaw.txt)
- [Captcha_Symbol.txt](file:///d:/Script/Test/FlowUltraTech/docs/Captcha_Symbol.txt)

---

## 1. ข้อมูลและโครงสร้างที่จำลองจากของจริง 100%

ไฟล์ Mock ทั้งสองถูกสร้างขึ้นโดยใช้โครงสร้าง DOM แท้ทั้งหมดของ TikTok LIVE Studio (ขนาดกว่า 700 KB ต่อไฟล์) ครอบคลุม:
- เลย์เอาต์หลักของ TikTok LIVE Studio (Scenes, Sources, Title bar "PUBG: BATTLEGROUNDS", Tools, Chat, Navigation)
- สไตล์ชีต, React bundle chunks และ CSS variables ทั้งหมด
- หน้าต่าง Inactivity Modal (`.tt-live-modalv2`, `.captcha-modal-content-UBaIok`) ในสถานะ **Active & Visible**
- ตัวเลขนับเวลาถอยหลังจริง (`.countdown-EXaokG` เริ่มนับจาก 04:59)
- รูปภาพ Captcha ของจริงความละเอียด 640×640 px (ฝังแบบ Base64 ไม่ต้องโหลดไฟล์ภายนอก)
  - รูป Jigsaw: เมื่อส่งให้ AI YOLOv8 (`jigsaw-model.onnx`) ประมวลผล จะตรวจพบพิกัดเป้าหมายจริง `X=461.55, Y=198.25`
  - รูป Symbol: ฝังภาพสัญลักษณ์ของจริงสำหรับตรวจจับวัตถุคู่เหมือน
- รางเลื่อนและปุ่มสไลเดอร์ (สำหรับ Jigsaw) ที่มีคลาสตรงตามตัวตรวจจับของ CDP ทุกตัว: `.secsdk-captcha-drag-icon`, `[role="slider"]`, `.captcha_verify_slide--slidebar`
- ระบบคำนวณและตรวจสอบระยะลาก: เมื่อ CDP จำลองการลากเมาส์สไลเดอร์ (`SimulateHumanDragAsync`) ไปถึงตำแหน่งเป้าหมาย ตัว Widget จะแสดงผล ✅ ผ่านการตรวจสอบ และปิด Modal ลงอัตโนมัติ

---

## 2. ไฟล์ที่เกี่ยวข้องในโปรเจกต์

| ไฟล์ | หน้าที่ |
|---|---|
| [start_mock_jigsaw.ps1](file:///d:/Script/Test/FlowUltraTech/start_mock_jigsaw.ps1) | **คลิกเดียวรัน**: เปิด Chrome/Edge พอร์ต 9221 โหลดหน้า Mock Jigsaw ทันที |
| [start_mock_symbol.ps1](file:///d:/Script/Test/FlowUltraTech/start_mock_symbol.ps1) | **คลิกเดียวรัน**: เปิด Chrome/Edge พอร์ต 9221 โหลดหน้า Mock Symbol ทันที |
| [mock_captcha_jigsaw.html](file:///d:/Script/Test/FlowUltraTech/docs/mocks/mock_captcha_jigsaw.html) | ไฟล์ HTML เต็มรูปแบบของ Jigsaw (711 KB) |
| [mock_captcha_symbol.html](file:///d:/Script/Test/FlowUltraTech/docs/mocks/mock_captcha_symbol.html) | ไฟล์ HTML เต็มรูปแบบของ Symbol (692 KB) |
| [serve_mocks.mjs](file:///d:/Script/Test/FlowUltraTech/docs/mocks/serve_mocks.mjs) | Local Mock HTTP Server (พอร์ต 8899) |

---

## 3. ขั้นตอนการทดสอบกับโปรแกรม FlowUltraTech

### ขั้นตอนที่ 1: เปิดหน้า Mock ผ่าน PowerShell
เปิด PowerShell ในโฟลเดอร์โปรเจกต์ แล้วสั่ง:
```powershell
# สำหรับทดสอบ Jigsaw (Slide):
.\start_mock_jigsaw.ps1

# หรือสำหรับทดสอบ Symbol:
.\start_mock_symbol.ps1
```
> สคริปต์จะค้นหา Google Chrome หรือ Microsoft Edge ในเครื่อง และเปิดหน้าต่างเบราว์เซอร์พร้อมเปิดพอร์ต CDP ที่ `--remote-debugging-port=9221` ให้อัตโนมัติ

---

### ขั้นตอนที่ 2: ทดสอบตรวจจับ Captcha บนโปรแกรม FlowUltraTech
1. เปิดโปรแกรม **FlowUltraTech**
2. ตั้งค่าหรือตรวจสอบให้มีโปรไฟล์/บัญชีที่ใช้ **CDP Port: 9221** (เช่น บัญชีแรก `Acc01`)
3. ในหน้าต่างโปรแกรม ให้ทดสอบฟังก์ชันดังนี้:
   - **ทดสอบ Scan Captcha**:
     กดปุ่ม **"🔍 สแกนเดี๋ยวนี้"** หรือสวิตช์ **"⏱️ Auto Detect Captcha"**
     - โปรแกรมจะตรวจจับ Inactivity Modal ทันที
     - การ์ดบัญชีจะแสดงแถบเตือนสีส้ม: `🚨 ตรวจพบ Captcha! (พบ 2 Element)`
     - แสดงเวลาถอยหลัง Countdown ที่ดึงมาจากหน้าเว็บแบบเรียลไทม์
   - **ทดสอบ Copy / Export Full HTML**:
     กดปุ่ม **"📋 คัดลอก Full HTML"** หรือ **"💾 Export ไฟล์"**
     - โปรแกรมจะดึง Full HTML ขนาดกว่า 700 KB ออกมาให้วิเคราะห์ได้ครบถ้วน 100%
   - **ทดสอบ Auto Solve Captcha (แก้จิ๊กซอว์อัตโนมัติ)**:
     กดปุ่ม **"⚡ แก้ Captcha อัตโนมัติ"**
     - ระบบจะดึงภาพจาก `#captcha-verify-image`
     - ส่งภาพเข้าประมวลผลด้วยโมเดล YOLOv8 ONNX (`jigsaw-model.onnx`)
     - AI คำนวณได้พิกัดจริง `X=461.55, Y=198.25` พร้อมความมั่นใจสูง
     - CDP สั่งจำลองการเคลื่อนที่ของเมาส์มนุษย์ (Bézier Curve) ลากปุ่ม `.secsdk-captcha-drag-icon` บนหน้าจอ
     - เมื่อลากถึงตำแหน่งเป้าหมาย หน้าเว็บจะแสดง ✅ และปิด Inactivity Modal ลง
     - โปรแกรม Sandbox จะตรวจจับว่า Modal หายไป และแจ้งว่า:
       `🎉 ✅ สำเร็จ! Inactivity Modal หายไปจากหน้าจอเรียบร้อยในรอบที่ 1`

---

## 4. การรัน Unit Tests ตรวจสอบระบบ
รันคำสั่ง:
```bash
dotnet test tests/FlowUltraTech.Tests/FlowUltraTech.Tests.csproj --filter "FullyQualifiedName~CaptchaMockDetectionTests"
```
เทสต์จะตรวจสอบความถูกต้องของ Selectors, Bounding Boxes, Countdown Text, และความถูกต้องของภาพ Base64 ทุกครั้งที่มีการแก้ไข
