# 02. Network & Proxy Isolation (การแยกเครือข่ายและ Proxy รายจอ)

## 1. ความซับซ้อนของทราฟฟิกใน TikTok LIVE Studio

TikTok LIVE Studio มีทราฟฟิกเครือข่าย 2 ประเภทหลัก:

1. **Web Traffic (HTTPS / WebSocket):**
   * เกิดขึ้นจากส่วนของ Chromium WebView (เช่น การล็อกอินบัญชี, การดึงข้อมูลห้อง, แชต, ของขวัญ, การยืนยันตัวตน)
2. **Media Stream Traffic (RTMP / UDP / WebRTC):**
   * เกิดขึ้นจาก C++ OBS Backend Engine ที่ทำการส่งเฟรมภาพวิดีโอสดเข้าสู่ TikTok Ingest Server
   * **สำคัญ:** การตั้งค่า Proxy ธรรมดาในระดับ Browser (เช่น `--proxy-server=...`) จะดักได้เฉพาะ Web Traffic เท่านั้น แต่ **RTMP Stream จะหลุดออกเน็ตจริง (IP Leak)** ซึ่งเป็นสาเหตุหลักที่ทำให้บัญชีโดนแบนทั้งฟาร์ม

---

## 2. วิธีการบังคับ Route ทราฟฟิก 100% ด้วย Proxifier

การใช้ **Proxifier** ช่วยให้สามารถดักจับทราฟฟิกของโปรแกรมทั้งหมด (ทั้ง Web Traffic และ RTMP/UDP Video Stream) บังคับให้ออกผ่าน Proxy ที่กำหนด:

### การตั้งค่า Proxification Rules สำหรับ TikTok LIVE Studio
ในโปรแกรม Proxifier สามารถกำหนดกฎ (Rule) กำหนดทราฟฟิกของ `TikTok LIVE Studio.exe`:

```xml
<!-- ตัวอย่าง Proxifier Configuration Rule -->
<Rule enabled="true">
    <Name>TikTok_Acc01</Name>
    <Applications>TikTok LIVE Studio.exe; live-studio.exe</Applications>
    <Action type="Proxy">101</Action> <!-- SOCKS5 Proxy 1 -->
</Rule>
```

### สิ่งที่ได้:
* ทุกทราฟฟิกเครือข่ายของ `TikTok LIVE Studio.exe` (รวมถึง Subprocess ทั้งหมด) จะถูกบังคับให้ออกผ่าน Proxy เสมอ
* ป้องกัน DNS Leak (เปิดออปชัน Remote DNS Through Proxy ใน Proxifier)
* ป้องกัน IP Leak ของ RTMP Ingest Stream เข้า TikTok อย่างเด็ดขาด
