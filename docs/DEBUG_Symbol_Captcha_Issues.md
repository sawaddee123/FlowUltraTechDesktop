# 🐛 Symbol Captcha Debug Report

**วันที่**: 2025-01-15  
**สถานะ**: มีปัญหา - ตรวจพบ Captcha แต่แก้ไม่ผ่าน

---

## 🔍 ปัญหาที่พบ

### 1. ⚠️ Mock JSON ไม่สมบูรณ์ (แก้ไปแล้ว ✅)

**ไฟล์**: `src/FlowUltraTech.Core/Config/mock_captcha_response.json`

**ปัญหา**: ไม่มี metadata สำหรับ coordinate mapping
```json
// ❌ เดิม (ไม่มี raw coordinates)
"symbol": {
  "symbol_positions": [[100, 110], [215, 140]],
  "time": 0.245,
  "confidence": 0.95
}
```

**แก้ไข**: เพิ่ม raw coordinates และ scaling metadata
```json
// ✅ แก้แล้ว
"symbol": {
  "symbol_positions": [[100, 110], [215, 140]],      // scaled แล้ว
  "raw_symbol_positions": [[201, 331], [432, 421]],  // ต้นฉบับ 640×640
  "image_width": 640,
  "image_height": 640,
  "display_width": 318,
  "display_height": 212,
  "scale_x": 0.496875,   // 318/640
  "scale_y": 0.33125,    // 212/640
  "time": 0.245,
  "confidence": 0.95
}
```

---

### 2. 🎯 การคำนวณพิกัด (Priority System)

**ไฟล์**: `CdpAutomationService.Captcha.cs` (บรรทัด 1308-1352)

โค้ดมี 3 ระดับการคำนวณพิกัด:

#### Priority 1: ใช้ Raw Coordinates (แม่นยำที่สุด ✅)
```csharp
if (hasRaw && origW > 0 && origH > 0)
{
    var rawPt = parsedApiResult!.RawClickPoints![i];
    clickX = rawPt.X * (domW / origW);  // แมปจาก 640×640 → DOM
    clickY = rawPt.Y * (domH / origH);
}
```

#### Priority 2: Server Scaled (ปรับ DOM)
```csharp
else if (wasServerScaled)
{
    var pt = clickPoints[i];
    double adjustX = (domW / srvW);  // adjust ถ้า DOM เปลี่ยน
    double adjustY = (domH / srvH);
    clickX = pt.X * adjustX;
    clickY = pt.Y * adjustY;
}
```

#### Priority 3: Fallback (มั่ว! ❌)
```csharp
else
{
    var pt = clickPoints[i];
    clickX = pt.X;
    clickY = pt.Y;
    
    // Scale เฉพาะตอนพิกัดเกิน DOM → ไม่แม่นยำ!
    if (clickX > domW || clickY > domH) {
        double sx = domW / refW;
        double sy = domH / refH;
        clickX = clickX * sx;
        clickY = clickY * sy;
    }
}
```

**ผลลัพธ์**: หลังแก้ Mock JSON แล้ว จะใช้ Priority 1 ที่แม่นยำ ✅

---

### 3. ⚠️ การตรวจสอบผล (Verification) - **ปัญหาหลัก!**

**ไฟล์**: `CdpAutomationService.Captcha.cs` (บรรทัด 1393-1436)

#### พฤติกรรมใน Mock HTML:
```javascript
// กด Submit → ถ้าถูก:
toast.textContent = '✅ Verification Successful!';

setTimeout(() => {
  if (modalWrapper) modalWrapper.style.display = 'none';  // ปิดหลัง 800ms
}, 800);
```

#### โค้ดตรวจสอบปัจจุบัน:
```csharp
await Task.Delay(1500);  // รอ 1.5 วินาที ✅

var verifyScript = @"(() => {
    const toast = document.querySelector('#jigsaw-toast, #symbol-toast, .tux-toast, [class*=""toast"" i]');
    const modal = document.querySelector('#captcha-modal-wrapper, .tt-live-modalv2-wrapper, #captcha-mount-point, #captcha-verify-container-main-page');
    const isModalHidden = !modal || 
                          window.getComputedStyle(modal).display === 'none' || 
                          modal.classList.contains('tt-live-modalv2-wrapper-exited');
    const toastText = toast ? (toast.textContent || '') : '';
    return JSON.stringify({ isModalHidden, toastText });
})()";

// ⚠️ ปัญหา: เช็คใน targetFrame (iframe) สำหรับ Symbol
string verifyJson = targetFrame != null && detection.Type == CaptchaType.Symbol
    ? await targetFrame.EvaluateExpressionAsync<string>(verifyScript)  // ← เช็คใน iframe
    : await targetPage.EvaluateExpressionAsync<string>(verifyScript);   // ← เช็คใน main page
```

#### ปัญหาที่เป็นไปได้:

1. **เช็ค Modal ผิด Frame**: 
   - Symbol widget อาจอยู่ใน iframe
   - แต่ modal wrapper อาจอยู่ที่ main page
   - ทำให้ไม่เจอ modal เลย → `isModalHidden = true` เสมอ → ผ่าน ✅ (แต่อาจจะผิด)

2. **Toast อยู่ใน iframe แต่อ่านไม่ได้**:
   - Toast อาจถูก render ใน iframe
   - แต่ตรวจจับไม่เจอเพราะ selector ไม่ตรง

3. **เงื่อนไขตรวจสอบหลวมเกินไป**:
   ```csharp
   isResolved = isModalHidden || 
                toastText.Contains("Success", StringComparison.OrdinalIgnoreCase) || 
                toastText.Contains("สำเร็จ", StringComparison.OrdinalIgnoreCase);
   ```
   - Mock ใช้ "Verification Successful!" → จับได้ ✅
   - แต่ถ้า TikTok จริงใช้ข้อความอื่น อาจจับไม่ได้

---

## 🔧 วิธีแก้ไขที่แนะนำ

### แก้ไขที่ 1: ตรวจสอบทั้ง iframe และ main page

```csharp
// Verification phase
await Task.Delay(1500);

var verifyScript = @"(() => {
    const toast = document.querySelector('#jigsaw-toast, #symbol-toast, .tux-toast, [class*=""toast"" i]');
    const modal = document.querySelector('#captcha-modal-wrapper, .tt-live-modalv2-wrapper, #captcha-mount-point, #captcha-verify-container-main-page');
    const isModalHidden = !modal || 
                          window.getComputedStyle(modal).display === 'none' || 
                          modal.classList.contains('tt-live-modalv2-wrapper-exited');
    const toastText = toast ? (toast.textContent || '') : '';
    return JSON.stringify({ isModalHidden, toastText });
})()";

// ✅ แก้ไข: เช็คทั้ง frame และ main page
string verifyJsonFrame = "";
string verifyJsonMain = "";

if (targetFrame != null && detection.Type == CaptchaType.Symbol)
{
    try { verifyJsonFrame = await targetFrame.EvaluateExpressionAsync<string>(verifyScript); } catch { }
}

try { verifyJsonMain = await targetPage.EvaluateExpressionAsync<string>(verifyScript); } catch { }

// รวมผลจากทั้งสองที่
bool isResolvedFrame = false;
bool isResolvedMain = false;
string feedbackMsg = "";

if (!string.IsNullOrEmpty(verifyJsonFrame))
{
    using var doc = JsonDocument.Parse(verifyJsonFrame);
    bool isModalHidden = doc.RootElement.TryGetProperty("isModalHidden", out var h) && h.GetBoolean();
    string toastText = doc.RootElement.TryGetProperty("toastText", out var t) ? t.GetString() ?? "" : "";
    isResolvedFrame = isModalHidden || 
                     toastText.Contains("Success", StringComparison.OrdinalIgnoreCase) || 
                     toastText.Contains("สำเร็จ", StringComparison.OrdinalIgnoreCase);
    if (!string.IsNullOrEmpty(toastText)) feedbackMsg = toastText;
}

if (!string.IsNullOrEmpty(verifyJsonMain))
{
    using var doc = JsonDocument.Parse(verifyJsonMain);
    bool isModalHidden = doc.RootElement.TryGetProperty("isModalHidden", out var h) && h.GetBoolean();
    string toastText = doc.RootElement.TryGetProperty("toastText", out var t) ? t.GetString() ?? "" : "";
    isResolvedMain = isModalHidden || 
                    toastText.Contains("Success", StringComparison.OrdinalIgnoreCase) || 
                    toastText.Contains("สำเร็จ", StringComparison.OrdinalIgnoreCase);
    if (!string.IsNullOrEmpty(toastText)) feedbackMsg = toastText;
}

bool isResolved = isResolvedFrame || isResolvedMain;
```

### แก้ไขที่ 2: เพิ่มการ Log เพื่อ Debug

```csharp
Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [CAPTCHA VERIFY] Frame Check: isResolved={isResolvedFrame}, feedback='{feedbackMsg}'");
Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [CAPTCHA VERIFY] Main Check: isResolved={isResolvedMain}, feedback='{feedbackMsg}'");
Log($"[{DateTime.Now:HH:mm:ss}] 🔍 [CAPTCHA VERIFY] Final Result: isResolved={isResolved}");
```

### แก้ไขที่ 3: เพิ่ม Retry Logic

```csharp
// Verification phase with retry
int maxRetries = 3;
bool isResolved = false;
string feedbackMsg = "";

for (int retry = 0; retry < maxRetries && !isResolved; retry++)
{
    await Task.Delay(1500 + (retry * 500));  // รอนานขึ้นทุกครั้ง
    
    // ... ทำการตรวจสอบ ...
    
    if (!isResolved && retry < maxRetries - 1)
    {
        Log($"[{DateTime.Now:HH:mm:ss}] 🔄 [CAPTCHA VERIFY] ยังไม่พบผล กำลังลองใหม่ครั้งที่ {retry + 2}/{maxRetries}...");
    }
}
```

---

## 🧪 การทดสอบ

### ทดสอบด้วย Mock:

```powershell
# 1. เปิด Mock Symbol Captcha
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\start_mock_symbol.ps1

# 2. เปิด FlowUltraTech และลอง Solve
# 3. ดู Console Log ว่ามี:
#    - "🔍 [CAPTCHA VERIFY] Frame Check: ..."
#    - "🔍 [CAPTCHA VERIFY] Main Check: ..."
#    - "✅ [CAPTCHA SOLVE] แก้ Captcha สำเร็จ" หรือ "⚠️ [CAPTCHA SOLVE] ..."
```

### ทดสอบด้วย E2E:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\test_symbol_e2e.ps1
```

---

## 📊 สรุป

| ปัญหา | สถานะ | แก้ไข |
|-------|-------|-------|
| Mock JSON ไม่มี raw coordinates | ✅ แก้แล้ว | เพิ่ม `raw_symbol_positions` และ metadata |
| Priority 3 Fallback มั่ว | ✅ แก้แล้ว | ใช้ Priority 1 (raw coordinates) |
| Verification เช็ค frame ผิด | ⚠️ รอแก้ | เช็คทั้ง iframe และ main page |
| ไม่มี log เพียงพอ | ⚠️ รอแก้ | เพิ่ม debug logging |
| ไม่มี retry logic | ⚠️ รอแก้ | เพิ่ม retry 3 ครั้ง |

---

## 🎯 ขั้นตอนต่อไป

1. ✅ แก้ Mock JSON (เสร็จแล้ว)
2. ⏳ แก้ Verification logic ให้เช็คทั้ง frame และ main page
3. ⏳ เพิ่ม debug logging
4. ⏳ ทดสอบกับ Mock
5. ⏳ ทดสอบกับ TikTok จริง

---

**หมายเหตุ**: ปัญหาหลักน่าจะอยู่ที่การตรวจสอบผล (Verification) ที่เช็คใน frame ผิด ทำให้แม้คลิกถูกแล้วก็อาจไม่เจอ modal หรือ toast
