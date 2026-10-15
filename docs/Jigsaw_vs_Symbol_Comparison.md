# 🔄 Jigsaw (Slide) vs Symbol Captcha - เปรียบเทียบการทำงาน

**วันที่**: 2025-01-15

---

## 📊 สรุปความแตกต่าง

| หัวข้อ | Jigsaw (Slide) | Symbol |
|--------|----------------|--------|
| **ข้อมูลจาก API** | `target_x` (1 ค่า) | `symbol_positions` (2 จุด) |
| **การคำนวณ** | 1D (X เท่านั้น) | 2D (X, Y) |
| **DOM Elements** | knob, track, piece | image box, submit button |
| **Action** | ลาก (drag) | คลิก 2 ครั้ง + submit |
| **Verification** | ตรวจ main page | ตรวจทั้ง frame + main page |
| **ความซับซ้อน** | **สูง** (คำนวณ drag ratio) | **ปานกลาง** (แค่ map พิกัด) |

---

## 🧩 Jigsaw (Slide Captcha)

### 1️⃣ ข้อมูลจาก API

```json
{
  "target_x": 228,              // ตำแหน่ง X ที่ต้องการวางชิ้นส่วน (DOM หรือ Raw)
  "raw_target_x": 461.5,        // พิกัดต้นฉบับจาก model (640px หรือ 552px)
  "image_width": 640,           // ขนาดภาพต้นฉบับ
  "display_width": 340,         // ขนาด DOM ที่ scale แล้ว
  "scale_x": 0.53125            // 340/640 = 0.53125
}
```

### 2️⃣ DOM Elements ที่ต้องหา

```csharp
// 1. ภาพ captcha
"#captcha-verify-image, .captcha_verify_img_slide, img[class*=\"slide\" i]"

// 2. ชิ้นส่วนจิ๊กซอว์ที่เคลื่อนที่
"#jigsaw-moving-piece, [class*=\"cap-absolute\"][draggable=\"true\"] img"

// 3. ปุ่มลากสไลเดอร์ (Knob)
"#captcha_slide_button, #jigsaw-slider-knob, .secsdk-captcha-drag-icon"

// 4. Track ของสไลเดอร์
"[class*=\"cap-rounded-full\"], #jigsaw-slidebar-track, .secsdk-captcha-drag-track"
```

### 3️⃣ การคำนวณพิกัด (3 Priority)

#### Priority 1: Raw Coordinates (แม่นยำที่สุด)
```csharp
if (parsedApiResult?.RawTargetX.HasValue == true && origW > 0)
{
    double rawX = parsedApiResult.RawTargetX.Value;  // เช่น 461.5px ใน 640px
    targetX = rawX * (domW / origW);                 // 461.5 * (340/640) = 244.9px
}
```

#### Priority 2: Server Scaled
```csharp
else if (parsedApiResult?.ScaleX.HasValue == true)
{
    // Server ส่ง 228 ที่ scale แล้วสำหรับ 340px
    // ถ้า DOM เปลี่ยนเป็น 350px
    targetX = 228 * (350 / 340) = 234.7px
}
```

#### Priority 3: Fallback
```csharp
else if (targetX > domW)
{
    // ถ้า targetX = 461 แต่ DOM = 340
    // scale: 461 * (340/640) = 244.8px
    double s = domW / origW;
    targetX = targetX * s;
}
```

### 4️⃣ คำนวณระยะลาก Knob (Drag Distance)

**ปัญหา**: ระยะที่ลาก knob ≠ ระยะที่ piece เคลื่อนที่!

```csharp
// TikTok Slider Algorithm (จาก 69901.3baa747e.js):
// 
// knobDelta = ระยะที่ลาก knob
// trackUsable = trackWidth - knobWidth  (ช่วงที่ knob เคลื่อนที่ได้)
// progress = knobDelta / trackUsable     (0.0 ถึง 1.0)
// pieceX = progress * imageWidth         (ตำแหน่ง piece บนภาพ)

// สูตรย้อนกลับ:
// ถ้าต้องการ pieceX = targetX
// progress = targetX / imageWidth
// knobDelta = progress * trackUsable
// knobDelta = targetX * (trackUsable / imageWidth)

double trackWidth = 340;   // จาก DOM
double knobWidth = 64;     // จาก DOM
double imageWidth = 340;   // DOM width
double usableTrack = trackWidth - knobWidth;  // 276px

double dragRatio = usableTrack / imageWidth;   // 276/340 = 0.8118
double knobDragDistance = targetX * dragRatio; // 228 * 0.8118 = 185.1px
```

**ตัวอย่าง**:
- ต้องการ piece อยู่ที่ X = 228px
- Track กว้าง 340px, Knob กว้าง 64px → Usable = 276px
- Drag Ratio = 276/340 = 0.8118
- ต้องลาก knob = 228 * 0.8118 = **185.1px**

### 5️⃣ การลาก (Drag) แบบ Human-like

```csharp
// 1. เริ่มต้น
await targetPage.Mouse.MoveAsync(startX, startY);
await targetPage.Mouse.DownAsync();

// 2. ลากแบบ easing curve (24 steps)
int steps = 24;
for (int step = 1; step <= steps; step++)
{
    double progress = (double)step / steps;
    double ease = 1 - Math.Pow(1 - progress, 3);  // Ease-out cubic
    decimal currentX = startX + (decimal)(knobDragDistance * ease);
    decimal currentY = startY + (decimal)(Random.Shared.NextDouble() * 2 - 1);  // Jitter
    await targetPage.Mouse.MoveAsync(currentX, currentY);
    await Task.Delay(Random.Shared.Next(12, 22));  // 12-22ms per step
}

// 3. ปล่อย
await Task.Delay(60);
await targetPage.Mouse.UpAsync();
```

### 6️⃣ การตรวจสอบผล

```csharp
await Task.Delay(1500);

// เช็คที่ main page เสมอ
string verifyJson = await targetPage.EvaluateExpressionAsync<string>(verifyScript);

// ตรวจ:
// 1. Modal หายไปหรือไม่ (display: none)
// 2. Toast แสดง "Success" หรือไม่
```

---

## 🔣 Symbol Captcha

### 1️⃣ ข้อมูลจาก API

```json
{
  "symbol_positions": [          // พิกัดที่ scale แล้ว (318×212)
    [100, 110],
    [215, 140]
  ],
  "raw_symbol_positions": [      // พิกัดต้นฉบับ (640×640)
    [201, 331],
    [432, 421]
  ],
  "image_width": 640,
  "image_height": 640,
  "display_width": 318,
  "display_height": 212,
  "scale_x": 0.496875,            // 318/640
  "scale_y": 0.33125              // 212/640
}
```

### 2️⃣ DOM Elements ที่ต้องหา

```csharp
// 1. ภาพ symbol
"#captcha-verify-image, #symbol-img-box, .captcha_verify_img_symbol"

// 2. ปุ่ม Submit/Confirm
"#symbol-submit-btn, .secsdk-captcha-submit-button, button[class*=\"verify-btn\" i]"

// 3. Toast (สำหรับตรวจผล)
"#symbol-toast, .tux-toast, [class*=\"toast\" i]"
```

### 3️⃣ การคำนวณพิกัด (3 Priority)

#### Priority 1: Raw Coordinates (แม่นยำที่สุด)
```csharp
if (hasRaw && origW > 0 && origH > 0)
{
    var rawPt = parsedApiResult!.RawClickPoints![i];
    
    // แมปจาก 640×640 → 318×212
    clickX = rawPt.X * (domW / origW);  // 201 * (318/640) = 99.87
    clickY = rawPt.Y * (domH / origH);  // 331 * (212/640) = 109.64
    
    Log($"แมปจาก Raw ({rawPt.X}, {rawPt.Y}) [640×640] → DOM ({clickX:F1}, {clickY:F1}) [318×212]");
}
```

#### Priority 2: Server Scaled
```csharp
else if (wasServerScaled)
{
    // Server ส่ง (100, 110) สำหรับ 318×212
    // ถ้า DOM เปลี่ยนเป็น 320×215
    var pt = clickPoints[i];
    double adjustX = (domW / srvW);  // 320/318 = 1.006
    double adjustY = (domH / srvH);  // 215/212 = 1.014
    clickX = pt.X * adjustX;         // 100 * 1.006 = 100.6
    clickY = pt.Y * adjustY;         // 110 * 1.014 = 111.5
}
```

#### Priority 3: Fallback
```csharp
else
{
    var pt = clickPoints[i];
    clickX = pt.X;  // 100
    clickY = pt.Y;  // 110
    
    // Scale เฉพาะตอนพิกัดเกิน DOM
    if (clickX > domW || clickY > domH)
    {
        double refW = (clickX > 552 ? 640.0 : 552.0);
        double refH = (refW == 640 ? 640.0 : 345.0);
        double sx = domW / refW;
        double sy = domH / refH;
        clickX = clickX * sx;
        clickY = clickY * sy;
    }
}
```

### 4️⃣ การคลิก

```csharp
for (int i = 0; i < clickPoints.Count; i++)
{
    // คำนวณพิกัดสัมบูรณ์บนหน้าจอ
    decimal targetClickX = imgBox.X + (decimal)clickX;
    decimal targetClickY = imgBox.Y + (decimal)clickY;
    
    Log($"คลิกจุดที่ {i + 1}: ({clickX:F1}, {clickY:F1}) (หน้าจอ: {targetClickX:F1}, {targetClickY:F1})");
    
    await targetPage.Mouse.ClickAsync(targetClickX, targetClickY);
    await Task.Delay(Random.Shared.Next(250, 450));  // รอก่อนคลิกจุดถัดไป
}

// หาปุ่ม Submit และกด
var submitHandle = await targetFrame.QuerySelectorAsync(submitSelector);
if (submitHandle != null)
{
    Log("กดปุ่มยืนยันผล (Submit / Confirm)...");
    await submitHandle.ClickAsync();
}
```

### 5️⃣ การตรวจสอบผล (ปัญหา!)

```csharp
await Task.Delay(1500);

// ⚠️ ปัญหา: เช็คใน targetFrame (iframe) สำหรับ Symbol
string verifyJson = targetFrame != null && detection.Type == CaptchaType.Symbol
    ? await targetFrame.EvaluateExpressionAsync<string>(verifyScript)  // ← iframe
    : await targetPage.EvaluateExpressionAsync<string>(verifyScript);  // ← main page

// ตรวจ:
// 1. Modal หายไปหรือไม่
// 2. Toast แสดง "Success" หรือ "สำเร็จ" หรือไม่
```

**ปัญหา**: 
- Symbol widget อยู่ใน iframe
- แต่ modal wrapper อาจอยู่ที่ main page
- Toast อาจอยู่ที่ main page หรือ iframe
- ทำให้เช็คใน iframe อย่างเดียวไม่เจอ

---

## 🎯 ความแตกต่างหลัก

### 1. **ความซับซ้อนของการคำนวณ**

**Jigsaw**:
```
API → Raw Target X (461.5px) 
    → Scale to DOM (244.9px)
    → คำนวณ Drag Ratio (0.8118)
    → Knob Distance (198.7px)
    → Drag 24 steps with easing
```

**Symbol**:
```
API → Raw Points [(201,331), (432,421)]
    → Scale to DOM [(100,110), (215,140)]
    → Click 2 ครั้ง
    → Submit
```

### 2. **Element Dependencies**

**Jigsaw**:
- ต้องหา: image, piece, knob, track (4 elements)
- ต้องคำนวณ: image size, piece offset, knob size, track size
- ต้องจำลอง: การลากแบบ human-like

**Symbol**:
- ต้องหา: image, submit button (2 elements)
- ต้องคำนวณ: image bounding box
- ต้องจำลอง: การคลิกธรรมดา

### 3. **Verification Complexity**

**Jigsaw**:
- Widget อยู่ใน main page → เช็ค main page เลย ✅
- Modal ปิดชัดเจน → ง่ายต่อการตรวจจับ

**Symbol**:
- Widget อาจอยู่ใน iframe → ต้องเช็คหลาย frame
- Toast อาจไม่แสดงชัดเจน → ยากต่อการตรวจจับ

### 4. **Physics & Timing**

**Jigsaw**:
```csharp
// Drag: 24 steps × (12-22ms) = 288-528ms
// Total: ~500-800ms
int steps = 24;
await Task.Delay(Random.Shared.Next(12, 22));  // per step
```

**Symbol**:
```csharp
// Click 2 times: 250-450ms between clicks
await Task.Delay(Random.Shared.Next(250, 450));  // between clicks
// Total: ~250-450ms
```

---

## 🐛 ปัญหาที่พบ

### Jigsaw: ✅ ทำงานดี
- Drag ratio คำนวณถูกต้อง (ตาม TikTok algorithm)
- Verification ชัดเจน (main page)
- ความแม่นยำสูง

### Symbol: ⚠️ มีปัญหา
1. Mock JSON ไม่มี `raw_symbol_positions` → ตก Priority 3 → คลิกผิดจุด
2. Verification เช็คใน iframe อย่างเดียว → ไม่เจอ modal/toast → ตัดสินผลผิด
3. ไม่มี retry logic → ถ้าพลาดครั้งเดียวก็ล้มเหลว

---

## 🔧 สรุป

| | Jigsaw | Symbol |
|---|--------|--------|
| **Complexity** | 🔴🔴🔴🔴⚪ (4/5) | 🔴🔴🔴⚪⚪ (3/5) |
| **Accuracy** | ✅ แม่นยำ | ⚠️ ยังมีปัญหา |
| **Verification** | ✅ ชัดเจน | ❌ ซับซ้อน |
| **ความเร็ว** | 🐢 ช้า (500-800ms) | 🐰 เร็ว (250-450ms) |
| **ความน่าเชื่อถือ** | ✅ สูง | ⚠️ ปานกลาง |

**ข้อสรุป**: Jigsaw ซับซ้อนกว่าแต่เสถียร, Symbol ง่ายกว่าแต่ยัง debug ไม่จบเพราะปัญหา verification
