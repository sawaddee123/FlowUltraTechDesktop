# 01. Multi-Instance Architecture (การเปิดหลายโปรแกรมพร้อมกัน)

## 1. สาเหตุที่ TikTok LIVE Studio บล็อกการเปิดซ้ำ

TikTok LIVE Studio มีกลไกป้องกันการเปิดโปรแกรมซ้ำ (Single Instance Enforcement) 2 ด่าน:

1. **Windows Named Mutex:** 
   เมื่อโปรแกรมเริ่มต้น จะทำการเรียก Win32 API เช่น `CreateMutexW(NULL, FALSE, L"TikTokLIVEStudio_SingleInstance_...")` หากพบว่ามี Mutex นี้อยู่ในระบบแล้ว ตัวโปรแกรมใหม่จะส่งคำสั่งไปยังหน้าต่างเดิมให้ Active ขึ้นมา แล้วปิดตัวโปรแกรมใหม่ลงทันที
2. **File Lock บน Chromium User Data Directory:**
   ตัว UI ของแอปสร้างขึ้นบน Chromium/Electron ซึ่งจะทำการล็อกไฟล์ฐานข้อมูล LevelDB, Cookies, และ Storage ใน `%APPDATA%\TikTok LIVE Studio` หากมีโปรเซสที่สองพยายามเข้าถึงโฟลเดอร์เดียวกัน Chromium จะปฏิเสธการทำงาน

---

## 2. ทางเลือกและวิธีการแก้ปัญหาในทางปฏิบัติ

### วิธีหลัก: การแยก User Data Directory (Direct Profile Isolation)
การเปิดหลาย Instance บนระบบปฏิบัติการโดยตรงอย่างเสถียรและปลอดภัยที่สุด โดยไม่ต้องติดตั้งโปรแกรมเสริมภายนอก:
1. **แยก Profile Directory:** รันแอปพลิเคชันโดยระบุพารามิเตอร์ของ Chromium เพื่อเปลี่ยนโฟลเดอร์ข้อมูลของแต่ละบัญชี:
   ```cmd
   "TikTok LIVE Studio.exe" --remote-debugging-port=9221 --user-data-dir="%LOCALAPPDATA%\TikTokStudioProfiles\Acc01"
   "TikTok LIVE Studio.exe" --remote-debugging-port=9222 --user-data-dir="%LOCALAPPDATA%\TikTokStudioProfiles\Acc02"
   ```
2. **ข้อดีของการใช้ Direct Profile:**
   * **ไม่ต้องพึ่งพา Kernel Driver หรือโปรแกรม 3rd-party:** รัน Native เร็วเต็มประสิทธิภาพ ไม่เสี่ยงต่อปัญหาความเข้ากันได้ของระบบ
   * **แยก Storage และ Session 100%:** Chromium จะจัดเก็บ Cache, Cookies, Local Storage, และการตั้งค่าของแต่ละโปรไฟล์แยกออกจากกันโดยเด็ดขาด
   * **รองรับการล็อกอินทั้ง 2 แบบ:** สามารถใช้การสแกน QR Code โดยตรงในแอป หรือใช้ระบบ Copy Login Callback จากเบราว์เซอร์ส่งเข้าแอป
   * **ควบคุมง่ายผ่าน CDP:** แต่ละจอเปิดพอร์ต Remote Debugging แยกกันอย่างชัดเจน เช่น `9221`, `9222` เป็นต้น
