# 04. Resource Optimization & Headless (การรีดประสิทธิภาพและทำกึ่ง Headless)

## 1. ทำไม TikTok LIVE Studio ไม่สามารถเป็น True Headless (CLI 100%) ได้?

เนื่องจากตัวแกนกลางของโปรแกรมคือ **OBS Engine (C++/libobs)** ซึ่งต้องพึ่งพา **DirectX 11 (D3D11)** ในการประกอบฉาก (Scene Compositing) และเรนเดอร์ภาพก่อนส่งเข้าตัวเข้ารหัส (Encoder) หากไม่มี Display/GPU Device หรือสั่งปิดการทำงานของ Graphics Subsystem ตัวแอปจะเกิดอาการแครชทันที

---

## 2. วิธีการทำ "Pseudo-Headless" (Windowless Mode) เพื่อลด Usage

เพื่อแก้ปัญหานี้และลดการกินทรัพยากรลงได้มากกว่า 50–70% เราสามารถใช้เทคนิคซ่อนหน้าต่าง UI:

### 1. สั่งซ่อนหน้าต่างด้วย Win32 API (`SW_HIDE`)
เมื่อโปรแกรมเปิดขึ้นมา เราใช้สคริปต์ค้นหา Window Handle (`HWND`) ของโปรแกรม แล้วสั่ง:
```powershell
# PowerShell ซ่อนหน้าต่าง
Add-Type @"
  using System;
  using System.Runtime.InteropServices;
  public class Win32 {
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  }
"@
# nCmdShow = 0 คือ SW_HIDE
[Win32]::ShowWindow($processHandle, 0)
```
* **ผลลัพธ์:** เมื่อหน้าต่างถูกซ่อน Chromium Compositor จะหยุดการคำนวณการวาดภาพบนหน้าจอ (0 FPS Rasterization) ช่วยประหยัด GPU และ CPU ฝั่ง UI ทั้งหมด **แต่พอร์ต CDP ยังตอบสนองและรับคำสั่งได้ 100%**

---

## 3. Chromium Performance Flags แนะนำสำหรับเปิดแอป

เราสามารถใส่ Arguments เข้าไปตอนสั่งเปิดโปรแกรมเพื่อตัดโหลดของ Chromium เพิ่มเติม:

```bash
--disable-gpu-vsync
--disable-smooth-scrolling
--disable-background-timer-throttling=false
--disable-renderer-backgrounding=false
--force-prefers-reduced-motion
```

---

## 4. ข้อจำกัดด้านการเข้ารหัสวิดีโอ (Hardware Video Encoding Limits)

ในการรันหลายๆ จอพร้อมกัน ตัวคอขวดที่แท้จริงคือ **ตัวเข้ารหัสวิดีโอ (NVENC)**:

* **NVIDIA GeForce (การ์ดจอสำหรับผู้บริโภค):** ถูกจำกัดจำนวน Concurrent Encoding Sessions ไว้ที่ **3–5 เซสชัน** ต่อการ์ด
* **แนวทางแก้ไขเมื่อต้องการรันเกิน 5 จอ:**
  1. **ใช้ Driver Patch (NVENC Patch):** ปลดล็อกข้อจำกัดของไดรเวอร์ NVIDIA ให้รันเซสชันไม่จำกัด (ขึ้นอยู่กับ VRAM ของการ์ด)
  2. **สลับไปใช้ Intel QuickSync (iGPU):** แบ่งบางจอให้ใช้ iGPU ของซีพียูในการ Encode
  3. **ลด Setting:** ปรับความละเอียดเป็น **720p 30fps** และ Bitrate **2000–2500 kbps** ช่วยลดโหลดของ VRAM และ Encoder Core ลงได้มากกว่าครึ่ง

---

## 5. Ultra-Light Background Login Mode & Local Profile Extraction

สำหรับการยืนยันตัวตนหรือล็อกอินบัญชีใหม่ โดยไม่ต้องการให้แอปเปิดหน้าจอหรือกิน GPU:

### 1. Ultra-Light Mode Flags
เปิดอินสแตนซ์ด้วย Argument สำหรับปิดการประมวลผลกราฟิกและเรนเดอร์ทั้งหมด:
```bash
--disable-gpu --disable-gpu-compositing --disable-software-rasterizer --disable-d3d11 --disable-accelerated-2d-canvas --disable-gpu-rasterization --no-sandbox
```
ร่วมกับการส่งคำสั่ง Win32 `SW_HIDE` ทันทีหลังเริ่มโปรเซส

### 2. วงจรการทำงานแบบสมบูรณ์
1. โปรแกรมผูก Protocol `live-studio-app://` ใน Windows ไปยังโปรไฟล์ที่ต้องการ
2. เปิดโปรเซสในโหมด Ultra-Light แบบซ่อนหน้าต่าง (GPU 0%, CPU/RAM ต่ำมาก) เพื่อรอรับ Callback จากเบราว์เซอร์
3. ผู้ใช้กดยืนยันตัวตนในเบราว์เซอร์
4. เบราว์เซอร์ส่ง Callback กลับมายังโปรเซสเบื้องหลังเพื่อแลกรับ Session จาก TikTok OAuth ผ่าน MetaSec Engine
5. ทันทีที่ล็อกอินผ่าน TikTok LIVE Studio จะเขียนข้อมูลลง `%LOCALAPPDATA%\TikTokStudioProfiles\<Folder>\TTStore\services.json`
6. `ProfileManagerService` ดักจับ Event และสกัดข้อมูลโปรไฟล์ (`userId`, `nickname`, `displayId`, `avatar`, `sessionId`) มาแสดงบน UI ทันที
7. ระบบจะทำการ **ปิดเบื้องหลังของโปรเซสอัตโนมัติ (Auto-Close)** คืนทรัพยากรเครื่อง 100% จนกว่าผู้ใช้จะกด Start สตรีมจริง
