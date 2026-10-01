# ผลตรวจ Captcha Symbol และ E2E

ตรวจเมื่อ 2026-10-02 โดยรัน Chrome จริงและ `CdpAutomationService` จาก Core โดยตรง

## ข้อมูลใน Captcha_Symbol.txt

ไฟล์มีโครงสร้างหน้าหลักและ Inactivity Modal แต่ไม่ครบสำหรับวิเคราะห์ widget ของ Symbol ที่กำลังแสดงผล:

| สิ่งที่ต้องใช้ | ข้อมูลที่พบ |
| --- | --- |
| Modal ที่เปิดอยู่ | wrapper เป็น `tt-live-modalv2-wrapper-exited` และ dialog มีสถานะ exit |
| Mount point | `<div id="captcha-mount-point" style="display: none;"></div>` ภายในว่าง |
| ภาพ challenge | ไม่มี `#captcha-verify-image` หรือภาพใน mount point |
| คำสั่ง Symbol | ไม่มีคำสั่งเลือกสองรูปทรงที่เหมือนกัน |
| Confirm ของ challenge | ไม่มีปุ่มภายใน mount point |
| Countdown | `00:00` |
| Challenge/session ID | ไม่พบรูปแบบ ID ที่ตัวตรวจจับใช้ |
| Iframe | ไม่มี `<iframe>` ใน dump |

การโหลด dump เป็น DOM แล้วตรวจด้วย Core จริงให้ `IsActive=false`, `Type=None` ซึ่งถูกต้องสำหรับข้อมูลนี้ หากต้องการยืนยัน selector ของ TikTok จริง ต้องบันทึก HTML รวมเนื้อหา iframe ขณะที่ Symbol แสดงอยู่ ก่อนปิดหรือแก้ challenge

## สิ่งที่แก้

- `start_mock_symbol.ps1`: ใช้ ASCII เพื่อให้ Windows PowerShell 5.1 อ่านได้ถูกต้อง ไฟล์เดิมเป็น UTF-8 ไม่มี BOM ทำให้ข้อความภาษาไทย/อีโมจิถูกอ่านผิดและส่วนเปิดเบราว์เซอร์กลายเป็นข้อความ สคริปต์เดิมจบ exit code 0 โดยไม่เปิดเบราว์เซอร์
- Launcher รองรับ `-Port`, `-Headless`, `-UserDataDirectory`, `-PassThru` และตรวจ `/json/version` กับ `/json/list` ก่อนแจ้งว่าพร้อม หากมี debugging browser อยู่แล้วจะเปิดแท็บ mock ผ่าน CDP
- ตัวตรวจจับสแกนแต่ละ frame ผ่าน CDP เพื่อเข้าถึง iframe คนละ origin และตรวจสถานะซ่อนของ iframe กับองค์ประกอบแม่
- การตรวจผล Symbol อ่านจาก frame ที่ส่งคลิกจริง หน้าหลักที่ไม่มี modal จึงไม่ทำให้คำตอบผิดถูกนับว่าสำเร็จ
- หน้า mock ตรวจพิกัดคู่ตัวอักษร `a` ในภาพ 640×640 โดยยอมรับทั้งสองลำดับและระยะคลาดเคลื่อน 35 พิกเซล ไม่ยอมรับสองจุดใดก็ได้ ภาพใช้ `object-fit: fill` เพื่อให้การฉายพิกัดจากภาพเต็มตรงกับ DOM 318×212 โดยไม่มีการครอป

## ผล E2E

ผ่าน 12 กรณี:

1. Launcher เปิดหน้า Symbol และ Core ตรวจพบ widget ที่โหลดแล้ว พร้อมภาพต้นฉบับและ countdown
2. ส่งพิกัดผิดผ่าน API fixture แล้ว Core คลิกสองจุด กด Confirm และรายงานล้มเหลว
3. ส่งพิกัดคู่ถูก `(450,230)`, `(480,390)` แล้วคลิก กด Confirm และปิด modal
4. หลังแก้แล้ว Core รายงาน inactive
5. HTML dump ต้นฉบับรายงาน inactive
6. Widget ที่องค์ประกอบแม่เป็น `visibility:hidden` รายงาน inactive
7. Symbol ใน iframe คนละ origin ตรวจพบได้ (`127.0.0.1` กับ `localhost`)
8. คำตอบผิดใน iframe ถูกปฏิเสธ
9. คำตอบถูกใน iframe ปิด modal ได้
10. หลังแก้ใน iframe รายงาน inactive
11. Iframe ที่เป็น `display:none` รายงาน inactive
12. Mock Jigsaw เดิมยังตรวจพบเป็น Jigsaw และดึงภาพได้

ตรวจคำขอ `/api/v1/captcha/symbol` ทั้ง 4 ครั้งว่ามีภาพและขนาด DOM รวมถึงตรวจหมุดสองจุดและข้อความผลในหน้า mock

E2E นี้ใช้ Chrome และ Core จริง โดยจำลองคำตอบของ solver HTTP จึงยืนยันการตรวจจับ การส่งภาพ การฉายพิกัด การคลิก และการตรวจผลบน fixture ได้ ยังไม่ได้ทดสอบโมเดล/solver จริง การยอมรับคำตอบจาก TikTok จริง หรือ UI ของแอป WPF

ทดสอบไฟล์ unit tests captcha เดิมแยกจาก App: ผ่าน 37 จาก 41 เทส อีก 4 เทสล้มเหลวเพราะไม่มีไฟล์ `extracted_sources/CaptchaJigsaw.html`, `CaptchaSymbol1.html`, `CaptchaSymbol2.html` ซึ่งอยู่นอก Git และไม่ได้สร้างข้อมูลแทนไฟล์จริง ส่วนคำสั่ง test ของโปรเจกต์หลักติด compiler 4.11 กับ `CommunityToolkit.Mvvm` analyzer ที่ต้องใช้ compiler 4.12 ก่อนเริ่มรันเทส

## รันซ้ำ

เปิด mock แบบหน้าต่างสำหรับใช้กับ FlowUltraTech (CDP 9221):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\start_mock_symbol.ps1
```

E2E ต้องมี Chrome/Edge และ .NET SDK 8 ขึ้นไป รันด้วยพอร์ตและ profile แยก แล้วปิดเบราว์เซอร์ของชุดทดสอบเมื่อจบ:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test_symbol_e2e.ps1
```

เครื่องนี้เตรียม SDK 8.0.425 ในโฟลเดอร์ชั่วคราว เนื่องจาก SDK ของระบบเป็น 7.0.306:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test_symbol_e2e.ps1 -DotnetPath "$env:TEMP\FlowUltraTech-dotnet8\dotnet.exe"
```

ผลรันล่าสุดและภาพหน้าจออยู่ใน `TestResults/symbol-e2e/run.log`, `symbol-active.png`, `symbol-wrong-answer.png` และผล unit tests ใน `captcha-unit.trx` (โฟลเดอร์ TestResults ถูก ignore โดย Git)
