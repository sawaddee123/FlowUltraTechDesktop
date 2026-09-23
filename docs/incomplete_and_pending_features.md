# รายการระบบที่ทำค้างไว้ / ทำเป็นโครงสร้างหลอกไว้ (Incomplete & Pending Features)

เอกสารสรุปรายการระบบและฟังก์ชันในโปรเจกต์ **FlowUltraTech (TikTok LIVE Studio Sandbox & Central Backend)** ที่ถูกสร้างขึ้นมาในลักษณะ "พอทำๆ ให้มี", เป็น Mock/Stub, หรือยังมีโค้ดไม่สมบูรณ์พร้อมใช้งานจริง โดยตัดระบบ *Stream Quality Monitor* และ *Rotate Captcha* ออกตามข้อกำหนด

---

## 📌 สรุปสถานะภาพรวม (Overview Matrix)

| ระบบ / ฟังก์ชัน | สถานะปัจจุบัน | ความสมบูรณ์ | สิ่งที่ขาด / จุดที่ค้างอยู่ |
| :--- | :---: | :---: | :--- |
| **1. Symbol Captcha AI Solver** | ⚠️ ทำค้างไว้ (ขาดโมเดล) | ~40% | ขาดไฟล์ ONNX ใน Node.js ทำให้รัน AI จริงไม่ได้ ปัจจุบันตกไปใช้ Mock พิกัดตายตัวเสมอ |
| **2. Broadcast Loop (ส่งข้อความวนซ้ำ)** | ❌ ยังไม่ได้เริ่มพัฒนา | 0% | มีเฉพาะใน Roadmap ยังไม่มีตัวจับเวลา Background Worker, คลังข้อความ หรือระบบสลับส่ง |
| **3. Auto Welcome แบบสมบูรณ์** | ⚠️ ทำไว้เฉพาะ Rule พื้นฐาน | ~50% | มี Event ดักจับ แต่ขาด LRU Cache ป้องกันการทักซ้ำ และขาด Batch Aggregation รวมชื่อผู้ชม |
| **4. AI Chatbot (OpenRouter LLM)** | ❌ ยังไม่ได้เริ่มพัฒนา | 0% | มีเฉพาะใน Roadmap ปัจจุบันตอบคอมเมนต์ได้แค่ Regex/คีย์เวิร์ดตายตัว ยังไม่เชื่อมต่อ AI |
| **5. Inactivity Warning Modal Handler** | ⚠️ ทำเฉพาะการตรวจจับ | ~40% | ตรวจจับตัวนับถอยหลังได้ แต่ยังไม่มี Action จำลองการกดยืนยันหรือปิด Modal อัตโนมัติ |
| **6. CDP In-Context Chat Sending** | ⚠️ Heuristic + คาดเดา | ~70% | พึ่งพา DOM Selector ที่อาจเปลี่ยนไปตามเวอร์ชัน และ Fallback fetch เป็นเพียงการคาดเดา |

---

## 🔍 รายละเอียดเชิงลึกแต่ละระบบ

### 1. 🧩 ระบบแก้ Symbol CAPTCHA ด้วย AI (Symbol Captcha AI Solver)

* **ไฟล์ที่เกี่ยวข้อง:**
  * Client: [`CdpAutomationService.Captcha.cs`](file:///d:/Script/Test/FlowUltraTech/src/FlowUltraTech.Core/Services/CdpAutomationService.Captcha.cs#L1101)
  * Backend API: [`src/routes/captcha.ts`](file:///d:/Script/java-script/FlowUltraTech/src/routes/captcha.ts#L88)
  * ML Logic: [`src/ml/symbol.ts`](file:///d:/Script/java-script/FlowUltraTech/src/ml/symbol.ts#L6)
  * Python Weights: [`python/symbol/models`](file:///d:/Script/java-script/FlowUltraTech/python/symbol/models)

* **สิ่งที่ทำไว้แล้ว:**
  * ฝั่ง Client (WPF) มีระบบตรวจจับ DOM สแกนหากรอบรูปสัญลักษณ์ `#symbol-img-box`, `img[class*="symbol"]`
  * มีระบบจำลองการคลิกเมาส์ตามพิกัดจุด `Mouse.ClickAsync()` และกดปุ่ม Submit ยืนยันผล
  * ฝั่ง Backend Node.js มี Endpoint `POST /api/v1/captcha/symbol` และโค้ดเตรียมโหลด ONNX
  * มีไฟล์ Mock Testbed สำหรับทดสอบ UI ([`start_mock_symbol.ps1`](file:///d:/Script/Test/FlowUltraTech/start_mock_symbol.ps1))

* **สิ่งที่ยังไม่เสร็จ / ค้างอยู่:**
  1. ในโฟลเดอร์ [`models/`](file:///d:/Script/java-script/FlowUltraTech/models) ของ Backend มีเพียงไฟล์ `jigsaw-model.onnx` เท่านั้น **ไม่มีไฟล์ `detect-symbol-model.onnx` และ `siamese-model.onnx`**
  2. ในโฟลเดอร์ [`python/symbol/models`](file:///d:/Script/java-script/FlowUltraTech/python/symbol/models) มีไฟล์น้ำหนัก PyTorch (`FastSAM-s.pt`, `FastSAM-x.pt`) ซึ่งยังไม่ได้ Export ออกมาเป็นโมเดล ONNX สำหรับ Node.js
  3. **พฤติกรรมปัจจุบัน:** หากบอทเปิดโหมด `LocalApiServer` แล้วส่งภาพ Symbol Captcha จริงเข้า API ตัวเซิร์ฟเวอร์จะ throw error และฝั่ง WPF จะ Catch แล้วสลับไปใช้ **Mock JSON พิกัดหลอก `[[100, 110], [215, 140]]`** เสมอ ทำให้ไม่สามารถแก้โจทย์จริงบนหน้าจอได้

* **แนวทางทำให้เสร็จ:**
  * Export โมเดล FastSAM/DINOv2 จาก Python เป็นไฟล์ ONNX นำมาวางที่ `models/detect-symbol-model.onnx` และ `models/siamese-model.onnx` หรือ
  * ทำ Python FastAPI Daemon แยกต่างหากที่รันโมเดล `.pt` โดยตรง แล้วชี้ให้ Client หรือ Backend ยิงไปหา

---

### 2. 📢 ระบบบรอดแคสต์วนซ้ำอัตโนมัติ (Broadcast Loop)

* **ไฟล์ที่เกี่ยวข้อง:**
  * แผนงาน: [`08_full_features_and_architecture_roadmap.md`](file:///d:/Script/Test/FlowUltraTech/docs/08_full_features_and_architecture_roadmap.md) (ข้อ 5)
  * การส่งข้อความ: [`CdpAutomationService.LiveChat.cs`](file:///d:/Script/Test/FlowUltraTech/src/FlowUltraTech.Core/Services/CdpAutomationService.LiveChat.cs#L36)

* **สิ่งที่ทำไว้แล้ว:**
  * มีเฉพาะฟังก์ชันส่งแชทผ่าน CDP (`SendChatMessageAsync`) เมื่อถูกสั่งการจากปุ่มหรือ Event

* **สิ่งที่ยังไม่เสร็จ / ค้างอยู่ (0% Code):**
  * ยังไม่มี Background Worker หรือ `PeriodicTimer` สำหรับนับเวลาวนลูปส่งข้อความ
  * ยังไม่มีการออกแบบ UI หรือ Data Model สำหรับ "คลังข้อความโปรโมชั่น" (Message Pool)
  * ยังไม่มีตรรกะการสลับข้อความแบบ Round-Robin หรือ Random เพื่อป้องกันการพิมพ์ข้อความเดิมซ้ำจนโดนแบนสแปม

* **แนวทางทำให้เสร็จ:**
  * เพิ่ม Model `BroadcastCampaign` ใน `FlowUltraTech.Core` ที่มี `IntervalSeconds`, `MessagesList`, `IsActive`
  * สร้าง Service จัดการ Background Timer ยิงข้อความผ่าน `ICdpAutomationService.SendChatMessageAsync` ราย Instance

---

### 3. 👋 ระบบทักทายผู้ชมอัตโนมัติแบบสมบูรณ์ (Auto Welcome Engine)

* **ไฟล์ที่เกี่ยวข้อง:**
  * Rule Engine: [`RuleEngine.cs`](file:///d:/Script/Test/FlowUltraTech/src/FlowUltraTech.Core/Services/RuleEngine.cs#L89)
  * Event Consumer: [`InstanceCardViewModel.LiveChat.cs`](file:///d:/Script/Test/FlowUltraTech/src/FlowUltraTech.App/ViewModels/InstanceCardViewModel.LiveChat.cs#L338)

* **สิ่งที่ทำไว้แล้ว:**
  * ฝั่ง Client มีการดักจับ Event `MemberJoin` จาก `TikTokLiveSharp`
  * มีฟังก์ชัน `EvaluateJoinRule` ใน `RuleEngine` เพื่อจับคู่กับแม่แบบข้อความ เช่น `"ยินดีต้อนรับคุณ @{User} เข้าสู่ห้องไลฟ์ครับ"`

* **สิ่งที่ยังไม่เสร็จ / ค้างอยู่:**
  1. **ขาด LRU / TTL Viewer Memory Cache:** ยังไม่มีระบบจำชื่อผู้ชมที่เคยทักไปแล้ว หากมีผู้ชมคนเดิมกดออกจากห้องแล้วแวะเข้ามาใหม่ในเวลาสั้นๆ บอทจะพิมพ์ทักซ้ำทันทีเมื่อพ้นระยะ Cooldown ทั่วไป
  2. **ขาด Batch Aggregation Mode:** เมื่อมีผู้ชมกดเข้าห้องพร้อมกันจำนวนมาก (เช่น ช่วงเปิดไลฟ์หรือแชร์ลิงก์) ระบบปัจจุบันจะประมวลผลทีละคน ซึ่งเสี่ยงต่อการโดน TikTok บล็อก Rate Limit การแชทอย่างรวดเร็ว ยังไม่มีระบบรวบชื่อเป็นกลุ่ม เช่น `"ยินดีต้อนรับคุณ @UserA, @UserB, @UserC เข้าสู่ไลฟ์ครับ!"`

* **แนวทางทำให้เสร็จ:**
  * สร้าง `ConcurrentDictionary<string, DateTime>` จำชื่อผู้ชมที่ทักแล้ว 15-30 นาที
  * เพิ่มบัฟเฟอร์คิวรวมชื่อผู้ชม 3-5 วินาที ก่อนประกอบข้อความส่งออกเป็นชุดเดียว

---

### 4. 🤖 ระบบตอบคอมเมนต์อัตโนมัติด้วย AI (OpenRouter LLM Integration)

* **ไฟล์ที่เกี่ยวข้อง:**
  * แผนงาน: [`08_full_features_and_architecture_roadmap.md`](file:///d:/Script/Test/FlowUltraTech/docs/08_full_features_and_architecture_roadmap.md) (ข้อ 8)

* **สิ่งที่ทำไว้แล้ว:**
  * มีเพียงการตอบแบบ Static Keyword ผ่าน Regex/Substring ใน [`RuleEngine.cs`](file:///d:/Script/Test/FlowUltraTech/src/FlowUltraTech.Core/Services/RuleEngine.cs)

* **สิ่งที่ยังไม่เสร็จ / ค้างอยู่ (0% Code):**
  * ยังไม่มีโค้ดเชื่อมต่อกับ OpenRouter API (`https://openrouter.ai/api/v1/chat/completions`)
  * ยังไม่มีกลไก Prompt Engineering (ป้อนบริบทสินค้า, ราคา, นโยบายร้านค้า, และข้อจำกัดความยาว)
  * ยังไม่มีตัวคัดกรองคำต้องห้าม (Profanity Filter) เพื่อความปลอดภัยของช่องไลฟ์

* **แนวทางทำให้เสร็จ:**
  * สร้าง `IOpenRouterService` รองรับโมเดลไวและประหยัด เช่น `google/gemini-2.0-flash`
  * เชื่อมคิวคำถามที่ RuleEngine ตอบไม่ได้ให้วิ่งเข้า AI พร้อมระบบจำกัด Cooldown

---

### 5. ⏳ ระบบจัดการ Inactivity Warning Modal (SecSDK Alert)

* **ไฟล์ที่เกี่ยวข้อง:**
  * ตรวจจับ DOM: [`CdpAutomationService.Captcha.cs`](file:///d:/Script/Test/FlowUltraTech/src/FlowUltraTech.Core/Services/CdpAutomationService.Captcha.cs#L420)

* **สิ่งที่ทำไว้แล้ว:**
  * ตรวจจับสถานะ `CaptchaType.InactivityModalWaiting` และอ่านค่านับถอยหลังได้เมื่อหน้าจอขึ้นเตือนการไม่ได้ขยับเมาส์/ไม่มีการเคลื่อนไหว

* **สิ่งที่ยังไม่เสร็จ / ค้างอยู่:**
  * ยังไม่มี Action จัดการอัตโนมัติ บอททำได้เพียงแจ้งเตือนใน Log และรอให้ Widget แคปต์ชาเรนเดอร์ขึ้นมาเอง หากเป็น Modal แจ้งเตือนธรรมดาที่ไม่มีแคปต์ชาตามมา บอทจะค้างรอโดยไม่ได้กดปุ่มยืนยันหรือขยับเคอร์เซอร์เพื่อเคลียร์ Modal

* **แนวทางทำให้เสร็จ:**
  * เพิ่มโค้ดค้นหาปุ่ม "ดำเนินการต่อ / I am still here" บน Modal แล้วสั่งคลิก หรือจำลองการ Scroll หน้าต่างเล็กน้อยเพื่อ Reset Inactivity Timer

---

### 6. 🌐 ความเสถียรของระบบส่งข้อความแชทผ่าน CDP (`SendChatMessageAsync`)

* **ไฟล์ที่เกี่ยวข้อง:**
  * [`CdpAutomationService.LiveChat.cs`](file:///d:/Script/Test/FlowUltraTech/src/FlowUltraTech.Core/Services/CdpAutomationService.LiveChat.cs#L52)

* **สิ่งที่ทำไว้แล้ว:**
  * มีชุดคัดเลือก Selector ค้นหาช่องแชท:
    `textarea[placeholder*='chat']`, `textarea[placeholder*='แชท']`, `div[contenteditable='true']`
  * มีการ Focus และจำลองการพิมพ์ Keyboard Type + กดปุ่ม Enter

* **สิ่งที่ต้องระวัง / กึ่งสำเร็จรูป:**
  * หากช่องกรอกข้อความถูกครอบด้วย Web Component หรือเปลี่ยนคลาสใหม่ บอทจะ Fallback ไปยิง API:
    ```javascript
    fetch('/webcast/room/chat/', { method: 'POST', body: JSON.stringify({ content: msg }) })
    ```
    ซึ่งเป็นการ Reverse-Engineer แบบคาดเดา (Speculative Fallback) และในเซสชันจริงของ TikTok Webcast มักจะติด Signature Verification ทำให้ Fallback นี้อาจส่งไม่ติดจริงเมื่อหน้าต่าง UI ไม่เปิดช่องให้พิมพ์

---

## 🎯 สรุปสิ่งที่ควรทำต่อตามลำดับความสำคัญ (Recommended Action Plan)

1. **ลำดับที่ 1 (ความสำคัญสูงสุด): ทำโมเดล Symbol Captcha ให้สมบูรณ์**
   * แปลงโมเดล FastSAM/DINOv2 เป็น ONNX วางลงใน Backend หรือรัน Python Server เพื่อให้บอทแก้โจทย์สัญลักษณ์จริงได้ ไม่ติด Mock
2. **ลำดับที่ 2: ระบบ Auto Welcome (Batching & LRU Cache)**
   * ใส่ Memory Cache ป้องกันการทักผู้ชมซ้ำ และรวมชื่อผู้ชมที่เข้าพร้อมกัน
3. **ลำดับที่ 3: ระบบ Broadcast Loop**
   * ทำ Background Worker ส่งข้อความโปรโมทตามรอบเวลา
4. **ลำดับที่ 4: เชื่อมต่อ AI Chatbot (OpenRouter)**
   * เสริมความฉลาดในการตอบคอมเมนต์คำถามยากๆ ที่อยู่นอกเหนือกฎคีย์เวิร์ด
