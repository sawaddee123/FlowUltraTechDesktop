# 07. การทดสอบและ Debug ระบบ Captcha

อัปเดตล่าสุด: 2026-10-20 · เวอร์ชันแอป: 1.7.0 (`<Version>` ใน [FlowUltraTech.App.csproj](../src/FlowUltraTech.App/FlowUltraTech.App.csproj))

เขียนใหม่จากโค้ดจริงแทน `archive/07_captcha_mock_testing_guide`, `08_captcha_symbol_verification`, `DEBUG_Symbol_Captcha_Issues`, `Symbol_Debug_Logging_Guide`, `Jigsaw_vs_Symbol_Comparison` คัดเฉพาะสาระที่ยังตรงกับโค้ด (ภาพรวมระบบอยู่ใน [06_captcha_system.md](06_captcha_system.md))

> เอกสารนี้ **ไม่ได้รันคำสั่งทดสอบใดๆ** ระหว่างเขียน ทุกขั้นตอนมาจากการอ่านสคริปต์/โค้ด คำสั่งที่ยิงเซิร์ฟเวอร์จริง (production) ถูกทำเครื่องหมาย ⛔ ไว้ในตาราง

## 1. ภาพรวมเครื่องมือ

| เครื่องมือ | ตำแหน่ง | ใช้ทำอะไร | ยิง network จริง |
|---|---|---|---|
| Mock Jigsaw/Symbol (HTML) | [docs/mocks/](mocks) | หน้า Studio จำลองพร้อม widget Captcha | ไม่ |
| `serve_mocks.mjs` | [docs/mocks/serve_mocks.mjs](mocks/serve_mocks.mjs) | เซิร์ฟเวอร์ static พอร์ต 8899 | ไม่ |
| `start_mock_jigsaw.ps1` / `start_mock_symbol.ps1` | **รากของ client repo** ([start_mock_jigsaw.ps1](../start_mock_jigsaw.ps1), [start_mock_symbol.ps1](../start_mock_symbol.ps1)) ไม่ใช่ใน `docs/mocks/` | เปิด Chrome/Edge พร้อม `--remote-debugging-port` และโหลด mock | ไม่ |
| Unit/Integration tests (xUnit) | [tests/FlowUltraTech.Tests/](../tests/FlowUltraTech.Tests) | ตรวจ selector, การ scale พิกัด, การ parse ผล, บางเคสเปิดเบราว์เซอร์จริง | บางเคส ⛔ |
| Symbol E2E | [tests/FlowUltraTech.Captcha.E2E/Program.cs](../tests/FlowUltraTech.Captcha.E2E/Program.cs) + [scripts/test_symbol_e2e.ps1](../scripts/test_symbol_e2e.ps1) | Chrome จริง + `CdpAutomationService` จริง + solver จำลองในเครื่อง | ไม่ |
| `python/full_debug.py` (backend) | [full_debug.py](file:///d:/Script/java-script/FlowUltraTech/python/full_debug.py) | benchmark/ดูผล solver ทุกภาพใน `images/symbol` และ `images/slide` | ไม่ |
| `python/symbol_service.py` (backend) | [symbol_service.py](file:///d:/Script/java-script/FlowUltraTech/python/symbol_service.py) | `--solve <ไฟล์>` แก้ภาพเดี่ยว, `/health` ของ daemon | ไม่ |
| `scripts/benchmark_simulation.py` (backend) | [benchmark_simulation.py](file:///d:/Script/java-script/FlowUltraTech/scripts/benchmark_simulation.py) | login → จอง lease → ยิง `/captcha/slide` N รอบ → `benchmark_result.json` | ⛔ production |
| `tests/*.mjs`, `tests/*.py` (backend) | [tests/](file:///d:/Script/java-script/FlowUltraTech/tests) | ทดสอบ API captcha / gatekeeper | ต้องมีเซิร์ฟเวอร์ ดู §5 |

## 2. Mock Captcha

### 2.1 ไฟล์

- [mock_captcha_jigsaw.html](mocks/mock_captcha_jigsaw.html) (~711 KB), [mock_captcha_symbol.html](mocks/mock_captcha_symbol.html) (~694 KB): ฝัง DOM/สไตล์ของ TikTok LIVE Studio และภาพ Captcha ขนาด 640×640 แบบ base64 — **ห้ามเปิดทั้งไฟล์ในตัวแก้ไข/เครื่องมืออ่านข้อความ** (ส่วน widget อยู่ราวบรรทัด 2890–3025)
- widget ใน mock แสดงภาพที่ **318×212 px** (`#jigsaw-img-box` / `#symbol-img-box`) ทำให้ใช้ทดสอบ logic scale 640→318 ได้
- dump จริงของ TikTok ที่เป็นที่มา: [docs/Captcha_Jigsaw.txt](Captcha_Jigsaw.txt), [docs/Captcha_Symbol.txt](Captcha_Symbol.txt) เป็นสถานะ modal ที่ **ปิดแล้ว** (`tt-live-modalv2-wrapper-exited`, `#captcha-mount-point` ซ่อนและว่าง) → ตรวจจับต้องได้ `IsActive=false` ส่วน widget ที่ active ใน mock เป็น fixture ที่เพิ่มขึ้น จึงยืนยัน DOM จริงของ TikTok หรือการยอมรับคำตอบจริงไม่ได้

| Element สำคัญใน mock | Jigsaw | Symbol |
|---|---|---|
| ภาพ | `#captcha-verify-image` | `#captcha-verify-image`, `#symbol-img-box` |
| ตัวควบคุม | `#jigsaw-slider-knob`, `#jigsaw-slidebar-track`, `#jigsaw-moving-piece`, `#jigsaw-cutout-hole` | `#symbol-submit-btn` (disabled จนคลิกครบ 2 จุด), `#symbol-reset-btn`, `#symbol-refresh-btn` |
| ผล | `#jigsaw-toast` | `#symbol-toast`, พินคลิกคลาส `.symbol-click-pin` |
| modal | `#captcha-modal-wrapper` (ซ่อนหลัง 800 ms เมื่อผ่าน) | เหมือนกัน |

### 2.2 กติกาตรวจคำตอบของ mock (อ่านจากสคริปต์ในไฟล์)

| Mock | เงื่อนไขผ่าน |
|---|---|
| Jigsaw | `TARGET_PIECE_X = 228`; ผ่านถ้า `|pieceX − 228| ≤ 35` **หรือ** `pieceX ≥ 193` (เงื่อนไขหลวม: ลากเกินเป้าก็ผ่าน) `pieceX = knobDrag ÷ 278 × 318`; ปล่อยเมาส์แล้วตรวจ |
| Symbol | แปลงจุดคลิกเป็นพิกัดภาพ 640×640 ต้องห่างจาก `(450,230)` และ `(480,390)` ไม่เกิน 35 px ทั้งสองจุด (ลำดับใดก็ได้); คลิกได้สูงสุด 2 จุด แล้วกด Confirm; ผิดขึ้น `Verification Failed...` และต้องกด Reset |

Mock ไม่ใช่ AI solver แค่ตรวจพิกัดที่คลิกมา จึงใช้ทดสอบ detect → scale → click/drag → verify ได้ แต่ไม่ได้ทดสอบความแม่นของโมเดล

### 2.3 วิธีเปิด

```powershell
# จากรากของ client repo (d:\Script\Test\FlowUltraTech)
.\start_mock_jigsaw.ps1                       # เปิด Chrome/Edge พอร์ต 9221 (คงที่) โหลด file:// ของ mock jigsaw
.\start_mock_symbol.ps1                       # พอร์ต 9221 โดยปริยาย
.\start_mock_symbol.ps1 -Port 9333 -Headless -UserDataDirectory "$env:TEMP\mock_sym" -PassThru
node docs\mocks\serve_mocks.mjs               # หรือ PORT=8899 / --port=8899 → http://localhost:8899/, /jigsaw, /symbol
```

พฤติกรรมจริงของสคริปต์:
- `start_mock_jigsaw.ps1`: ค้น Chrome ก่อน แล้ว Edge; profile ชั่วคราว `%TEMP%\TiktokStudio_MockProfile_9221`; **พอร์ตตายตัว 9221** ไม่มีพารามิเตอร์
- `start_mock_symbol.ps1`: พารามิเตอร์ `-Port` (1024–65535, ค่าเริ่ม 9221), `-Headless`, `-UserDataDirectory`, `-PassThru`; ถ้ามี debugging browser บนพอร์ตนั้นอยู่แล้ว จะเปิดแท็บใหม่ผ่าน `/json/new` แทนการเปิดเบราว์เซอร์ใหม่; รอ `/json/version` + `/json/list` พร้อมสูงสุด 20 วินาที; ไฟล์เป็น ASCII เพื่อรองรับ Windows PowerShell 5.1
- `serve_mocks.mjs`: ให้บริการ `/`, `/jigsaw` (`/mock_captcha_jigsaw.html`), `/symbol` (`/mock_captcha_symbol.html`) พอร์ต 8899 (หน้า index แนะนำคำสั่ง `chrome.exe --remote-debugging-port=9221 http://localhost:${PORT}/jigsaw`)

### 2.4 ทดสอบกับแอป

1. เปิด mock ตามข้างบน (พอร์ตต้องตรงกับ `CdpPort` ของบัญชีที่จะใช้)
2. ในแอป บัญชีนั้นต้องมีสถานะ `Running` (การสแกนทั้งหมดคืนผลว่างถ้า `Status != Running`) และต้องล็อกอิน + มี lease (`ActiveLeaseId`) ถ้าจะให้ยิง solver จริง
3. ปุ่ม "สแกนเดี๋ยวนี้" ([MainWindow.xaml](../src/FlowUltraTech.App/MainWindow.xaml) L1202) หรือ checkbox "Auto Detect" / "Auto Solve" (timer จริง 2 วินาที ไม่ใช่ 5)
4. ถ้าไม่ต้องการสร้างบัญชี: แท็บ Debug ([DebugViewModel.cs](../src/FlowUltraTech.App/ViewModels/DebugViewModel.cs), `TargetPort` เริ่มที่ 9221) มี `CheckCdpStatus`, `ExecuteScript`, network/cookies/HTML และ detect Captcha ต่อพอร์ตที่ระบุ (ฟังก์ชัน detect ยืนยันจากชื่อเทสต์ `DebugViewModel_DetectCaptcha_*`)
5. หมายเหตุ: การแก้จริงผ่านแอปจะ **เสียโควต้า** บนเซิร์ฟเวอร์ (หักตอนคำนวณสำเร็จ) และ mock ไม่ใช่ TikTok ผลที่ส่ง `verify-result` จึงเป็นของ fixture
6. ยังไม่ได้ทดสอบ flow นี้ในรอบเขียนเอกสาร (ยังไม่ยืนยันจากการรันจริง)

## 3. ชุดทดสอบ Client (xUnit, `net8.0-windows`)

โปรเจกต์: [FlowUltraTech.Tests.csproj](../tests/FlowUltraTech.Tests/FlowUltraTech.Tests.csproj) (xunit 2.5.3; อ้างอิง Core และ App)

| ไฟล์ | จำนวน `[Fact]` | เนื้อหา |
|---|---|---|
| [CaptchaMockDetectionTests.cs](../tests/FlowUltraTech.Tests/CaptchaMockDetectionTests.cs) | 19 | selector/regex ใน mock, countdown, session id, topmost, dump ที่ปิดแล้วต้อง inactive, data-URL WebP |
| [CaptchaMockSolverTests.cs](../tests/FlowUltraTech.Tests/CaptchaMockSolverTests.cs) | 19 | `ParseSolverApiResponse`, `CoordinateScaling_*` (กัน double-scaling), ระยะลาก knob, ขนาด WebP lossy (`552x344`), selector TUX; เคส `SolveCaptchaAsync_RealTikTokSymbol2Html_*` เปิด Edge/Chrome จริง + HttpListener ในเครื่อง |
| [CaptchaLiveApiSolverTests.cs](../tests/FlowUltraTech.Tests/CaptchaLiveApiSolverTests.cs) | 8 | ค่าเริ่มต้น AppSettings, offline port, **`*_RealLiveE2E_*`, `*_RealTikTok*_AgainstLiveServer_*`, `SolverApi_WhenTokenExpiredOrInvalid_*` ⛔ ยิง `<server-host>:8001` ด้วย login ของ root ที่ฝังในไฟล์** |
| [CaptchaLeaseHeaderTests.cs](../tests/FlowUltraTech.Tests/CaptchaLeaseHeaderTests.cs) | 1 | รับ `instanceLeaseId` และจัดการพอร์ตออฟไลน์อย่างสุภาพ |
| [DebugViewModelAndCdpTests.cs](../tests/FlowUltraTech.Tests/DebugViewModelAndCdpTests.cs) | 41 | DebugViewModel, network/cookies/storage, `InstanceCardViewModel_CheckCaptcha_*` (ใช้ fake CDP ไม่ต่อจริง) และส่วนอื่นของ UI |
| [LiveSessionWatchdogE2ETests.cs](../tests/FlowUltraTech.Tests/LiveSessionWatchdogE2ETests.cs) | 1 | Backoffice/desktop session (ไม่ใช่ Captcha โดยตรง) |

คำสั่ง (ปลอดภัย ไม่ยิง production): 

```powershell
dotnet test tests\FlowUltraTech.Tests\FlowUltraTech.Tests.csproj --filter "FullyQualifiedName~CaptchaMockDetectionTests"
dotnet test tests\FlowUltraTech.Tests\FlowUltraTech.Tests.csproj --filter "FullyQualifiedName~CaptchaMockSolverTests"
dotnet test tests\FlowUltraTech.Tests\FlowUltraTech.Tests.csproj --filter "FullyQualifiedName~InstanceCardViewModel_CheckCaptcha|FullyQualifiedName~DebugViewModel_DetectCaptcha"
```

ข้อควรระวัง:
- เทสต์หลายตัวอ่านไฟล์ที่ **ไม่อยู่ใน Git**: `extracted_sources/CaptchaJigsaw.html`, `CaptchaSymbol1.html`, `CaptchaSymbol2.html`, `Acc08_Symbol_SameShape.webp`, `Full html captcha.html` (โฟลเดอร์ `extracted_sources/` ถูก ignore ใน [.gitignore](../.gitignore) บรรทัด 37; เครื่องนี้มี 4 ไฟล์แรก ไม่มี `Full html captcha.html`) → บนเครื่องอื่นเทสต์เหล่านี้จะล้ม; โค้ดมี path สำรองแบบ absolute `D:\Script\Test\FlowUltraTech\...`
- เคสที่เปิดเบราว์เซอร์ต้องมี Edge/Chrome ที่ path มาตรฐาน; `*_RealLiveE2E_AgainstPort9221` ใช้พอร์ต 9221 ตายตัว (อย่ารันขณะมี Studio ใช้พอร์ตนี้)
- ⛔ `CaptchaLiveApiSolverTests` มีข้อมูล login ฝังในซอร์ส ไม่ควรรันพร่ำเพรื่อ เพราะสร้าง log/หักโควต้าบน production และควรย้ายไป env/secret
- บันทึกเก่า (`archive/08_*`, 2026-10-02) ระบุว่าเคยรันได้ 37/41 เทสต์ captcha (ล้ม 4 เพราะขาด `extracted_sources`) และมี compiler analyzer ขัดกัน (CommunityToolkit.Mvvm ต้อง compiler 4.12) — ยังไม่ได้รันซ้ำ จึงไม่ยืนยันสถานะปัจจุบัน

### Symbol E2E (ไม่ยิง network ภายนอก)

[test_symbol_e2e.ps1](../scripts/test_symbol_e2e.ps1): สุ่มพอร์ตว่างผ่าน `TcpListener` → `start_mock_symbol.ps1 -Headless -Port <สุ่ม> -UserDataDirectory <temp> -PassThru` → `dotnet run --project tests/FlowUltraTech.Captcha.E2E -- <root> <port>` → บันทึก `TestResults/symbol-e2e/run.log` และภาพ `symbol-active.png`, `symbol-wrong-answer.png` → ปิดเฉพาะเบราว์เซอร์ที่ตนเปิดและลบ profile ชั่วคราว (พารามิเตอร์ `-DotnetPath` เมื่อ SDK ในระบบเก่ากว่า .NET 8)

[Program.cs](../tests/FlowUltraTech.Captcha.E2E/Program.cs) ใช้ `CdpAutomationService` จริงกับ `SymbolFixtureServer` (HttpListener) ที่ตอบ `/api/v1/captcha/symbol` ด้วยพิกัดคงที่ (ถูก `(450,230),(480,390)` / ผิด `(80,80),(150,150)`) และเสิร์ฟ mock เป็น `/symbol`, `/host` (iframe ข้าม origin `127.0.0.1`↔`localhost`) ตรวจ 12 สถานการณ์:

1. detect widget symbol (ภาพต้นฉบับต้องเป็น `640x640`, countdown ต้องมี)
2. คำตอบผิดถูกปฏิเสธ (คลิกจริง 2 จุด, API ถูกเรียกครั้งเดียว, payload มีภาพและ displayWidth/Height)
3. Reset แล้วคำตอบถูกปิด modal
4. หลังแก้ → inactive
5. dump `Captcha_Symbol.txt` → inactive
6. ancestor `visibility:hidden` → inactive
7–10. iframe ข้าม origin: detect, ผิด, ถูก, หลังแก้ inactive
11. iframe `display:none` → inactive
12. regression: mock jigsaw ยังตรวจพบ `Jigsaw` พร้อมภาพ

ผลที่ยืนยัน: การตรวจจับ ส่งภาพ ฉายพิกัด คลิก และตรวจผลบน fixture; **ไม่ได้ทดสอบ** โมเดล/solver จริง, การยอมรับของ TikTok จริง, UI WPF

## 4. Debug logging ที่ยังตรงกับโค้ด

ที่มาของ log: `progressLogger` ของ `SolveCaptchaAsync` → `AppendConsole` (console ของการ์ด) + `IInstanceLogger.Info("CaptchaSolve", ...)` (ไฟล์ log รายบัญชี) ส่วนการสแกนเขียน tag `Captcha`

### 4.1 Tag และ prefix

| Prefix ใน log | ขั้นตอน |
|---|---|
| `[CAPTCHA SOLVE]` | ภาพรวม: เริ่ม/ตรวจ detection/หน่วง/ผล/error |
| `[SOLVER API]` | parse ผลจาก backend (`คำนวณตำแหน่งสไลเดอร์สำเร็จ`/`คำนวณพิกัดสัญลักษณ์สำเร็จ`, ID) |
| `[SYMBOL DEBUG]` | frame/page ที่เจอ, bounding box, ขนาด DOM/ภาพต้นฉบับ, `hasRaw`/`wasServerScaled`, clamp |
| `[SYMBOL P1]` / `[SYMBOL P2]` / `[SYMBOL P3]` | สูตรพิกัดที่ใช้ต่อจุด (ดู 06 §6) |
| `[SYMBOL CLICK]`, `[SYMBOL SUBMIT]` | พิกัดหน้าจอ, การคลิก, การกด Confirm |
| `🧩 [CAPTCHA SOLVE]` (Jigsaw) | แมป target X, ข้อมูล piece, `DragRatio`, `KnobDrag`, สถานะ transform ของ piece หลังลาก |
| `[VERIFY]`, `[VERIFY FRAME]`, `[VERIFY MAIN]` | ผลตรวจ: `hasModal`, `modalDisplay`, `isModalHidden`, `toastText`, `isResolved`, เวลารวม |
| `[Auto-Detect]`, `[Auto Monitor]` | ฝั่ง ViewModel: เวลาที่สแกนใช้, timeout 8s/15s, สถานะปกติ (log ที่ scan #1 และทุก 12 รอบ) |
| `🚨 [REAL CAPTCHA ACTIVE]` | ใน `DetectCaptchaElementsAsync`: type, countdown, session id, ขนาด HTML, element ทีละรายการ |

### 4.2 วิธีอ่านผล (ตารางวินิจฉัย)

| อาการ | ดูที่ | สาเหตุที่เป็นไปได้ตามโค้ด |
|---|---|---|
| ไม่เจอ Captcha ทั้งที่เห็นบนจอ | `[REAL CAPTCHA ACTIVE]` ไม่ขึ้น; `State=HiddenOrUnmounted` | ancestor ซ่อน/`exited`; widget อยู่ใน iframe ที่ซ่อน; selector/ข้อความเปลี่ยน |
| `InactivityModalWaiting` ค้าง | Type ไม่เปลี่ยนเป็น Jigsaw/Symbol | modal เปิดแต่ SecSDK ยังไม่ render ภาพ/ข้อความ |
| ไม่เจอ image element | `[SYMBOL DEBUG] ❌ ไม่พบ image element` | selector ไม่ตรง หรือ widget ยังไม่โหลด |
| คลิก/ลากผิดตำแหน่ง | `hasRaw`, `P3`, `ถูก clamp` | ตกสูตร P3 (backend ไม่ส่ง `raw_*`/`image_width`) ควรใช้ P1 |
| ตอบ 403 | `Solver API ตอบกลับสถานะ: 403` | ไม่มี/หมดอายุ/ไม่ตรงผู้ใช้/HWID ของ `X-Instance-Lease` |
| ตอบ 429 | ข้อความโควต้า | `QUOTA_EXCEEDED` หรือ `PROFILE_QUOTA_EXCEEDED` (ต่อจอ 200/วัน, UTC) หรือ rate limit (120/5 นาที ต่อ user, 25/5 นาที ต่อ lease) |
| ตอบ 422 | `ServerResponseJson` | solver หาตำแหน่งไม่ได้ (`Invalid slide position` / `Invalid symbol positions`) |
| `Token expired` | log ของ solver | client จะ refresh แล้ว retry 1 ครั้ง ถ้ายังไม่ผ่านดู RefreshToken |
| ช้า/timeout | เวลาที่สแกน/แก้ | HTTP 30 วินาที; Python daemon เย็น (โหลดโมเดล ≤25 วินาทีตอนสตาร์ต) |
| `isResolved` เป็น True ทั้งที่ widget ยังอยู่ | `[VERIFY MAIN] hasModal=false` | ความเสี่ยง false-positive ของ verify (06 §9) |
| ไม่พบปุ่ม Submit | `[SYMBOL SUBMIT] ไม่พบปุ่ม Submit!` | selector ผิดหรือปุ่ม disabled |

ข้อมูลใน log ที่ **ไม่เป็นจริงแล้ว** ในเอกสารเก่า: ข้อเสนอแก้ "เพิ่ม retry 3 ครั้งใน verify" ยังไม่มีในโค้ด (verify รอ 1.5 วินาทีครั้งเดียว); "Mock JSON ไม่มี raw coordinates → ตก Priority 3" ใช้ไม่ได้อีกเพราะไม่มี Mock fallback แล้ว (Priority 3 ปัจจุบันเกิดเมื่อ backend ไม่ส่ง `raw_*` หรือ `scale_x`)

### 4.3 เครื่องมือเสริมสำหรับตรวจ DOM/ภาพ

- ปุ่ม/คำสั่งในการ์ด: Copy/Export Full HTML (`forceFullHtml` ดึง HTML ของ frame ที่พบ Captcha), บันทึก/คัดลอกรูป Captcha, เปิดโฟลเดอร์ภาพ (`Pictures\TikTokStudioCaptures`), เล่น/หยุดเสียงเตือน, ส่ง Telegram เมื่อ detect/solved/failed
- ตั้ง `AutoSaveCaptchaImage` (ค่าเริ่ม true) เพื่อเก็บภาพจริงไว้ทำชุดทดสอบ (นามสกุล `webp` ถ้าเป็น RIFF/WEBP)
- Backend เก็บภาพทุกคำขอไว้ที่ `storage/captchas/` (ไฟล์ `*_placed.png` คือภาพซ้อนตำแหน่ง) และ `CaptchaLog` ใน MongoDB (TTL 30 วัน) ให้ superadmin ดูย้อนหลัง

## 5. เครื่องมือตรวจ Captcha ฝั่ง Backend

Repo: `d:/Script/java-script/FlowUltraTech`

| เครื่องมือ | คำสั่ง/ตัวอย่าง | หมายเหตุ |
|---|---|---|
| Benchmark/ดูผล solver ทุกภาพ | `python python/full_debug.py --type symbol\|slide\|all [--images-dir images] [--output python/full_debug] [--threshold 0.70]` | ใช้ `images/symbol` (103 ไฟล์) และ `images/slide` (44 ไฟล์) เขียน `summary.csv`, `speed_summary.csv`, ภาพผลสุ่มชื่อ; คอลัมน์ `status` บอกแค่ว่าได้ผล (ไม่มี ground truth); ไม่ผ่าน Node |
| Symbol CLI | `python -m python.symbol.main --screen <ภาพ> [-t 0.70] [-v] [--save out.jpg] [--debug --debug-path dir]` | ชื่อตัวเลือกที่พบใน `main.py` คือ `--screen`, `--image`, `-t/--threshold`, `-v/--visualize`, `--save`, `--debug`, `--debug-path` (ไม่ได้ตรวจว่าตัวไหนบังคับ); ขั้นตอน debug บันทึกภาพ preprocess/detect/silhouette/best pair |
| Symbol daemon | `python python/symbol_service.py --port 3008` / `--solve <ภาพ>` ; `GET http://127.0.0.1:3008/health` | ตอบ `status`, `model_detector: FastSAM-s.pt`, `model_extractor: dinov2_vits14`, `device`; `POST /solve` ตอบ 200 หรือ 422 พร้อม `symbol_positions` |
| Slide CLI | `python -m python.slide.main` | ดู [python/slide/main.py](file:///d:/Script/java-script/FlowUltraTech/python/slide/main.py) (ไม่ได้ตรวจตัวเลือกละเอียด) |
| Build + รัน | `npm run build` แล้ว `npm start`; พอร์ตเริ่มต้นเซิร์ฟเวอร์ 8001 (`src/config/env.ts`) | เทสต์ `.mjs` import จาก `dist/` จึงต้อง build ก่อน |
| `tests/test-symbol.mjs` | `npm run test:symbol` (ตั้ง `AUTH_URL`, `CAPTCHA_URL`) | อ่านภาพจาก `images/symbol`; ส่งโดยไม่แนบ lease → ผ่านได้เฉพาะ user ระดับ root |
| `test-backend-full.mjs` | `npm run test:backend` | ครอบคลุม gatekeeper: ไม่มี lease → 403, lease ปลอม → 403, lease จริง → ไม่ใช่ 403 และมี `CaptchaLog` ใน MongoDB (เชื่อม MongoDB จริง ตรวจ `.env` ก่อนรัน) |
| `tests/test_crud_and_captchas.py` | Python | ทดสอบเก็บภาพ/สิทธิ์ superadmin; **path ภาพชี้โฟลเดอร์เก่า `SimpleSolveCaptchaProject`** |
| ⛔ `scripts/benchmark_simulation.py` | `SERVER_URL`, `ROOT_USER`, `ROOT_PASS` เป็น env | ค่า default ในไฟล์ชี้ production และมีรหัสผ่านฝัง — อย่ารันโดยไม่จำเป็น |

สคริปต์ที่ **ล้าสมัย/ใช้ไม่ได้ตามที่เขียน**: `npm test`, `test:simple`, `test:all` ยังชี้พอร์ต `3005/3006` และ path `/login` ของยุคก่อน; `tests/scratch_*.mjs` และ `test-webp-decode.mjs` อ้างโฟลเดอร์เก่า (`TiktokStudioSandbox/extracted_sources`, `_archive/...`); README ใน `python/` อธิบาย API เก่า

สคริปต์ Python ใน **client repo** `scripts/` ที่อ่านแล้ว: `inspect_captcha_log.py` และ `test_model.py` ทำ SSH เข้าเซิร์ฟเวอร์ production, `test_live_slide_api.py` login เข้า production — ทั้งหมดฝัง credential ในไฟล์ ไม่แนะนำให้รัน (ไฟล์อื่นในโฟลเดอร์ เช่น `check_remote_server.py`, `deploy_remote_server.py` ยังไม่ได้ตรวจ); `inspect_dom.py` เป็นสคริปต์ BeautifulSoup ท้องถิ่นอ่าน `extracted_sources/CaptchaJigsaw.html`

## 6. เปรียบเทียบ Jigsaw กับ Symbol (ที่ยังจริงในโค้ด)

| หัวข้อ | Jigsaw (Slide) | Symbol |
|---|---|---|
| Endpoint | `/api/v1/captcha/slide` | `/api/v1/captcha/symbol` |
| ข้อมูลจาก API ที่ใช้ | `raw_target_x` (หรือ `target_x`/`position[0]`) | `raw_symbol_positions` (หรือ `symbol_positions`) 2 จุด |
| มิติของพิกัด | 1 มิติ (X) | 2 มิติ (X,Y) |
| Solver | ONNX YOLOv8 ใน Node (CPU) | Python daemon FastSAM + DINOv2 |
| องค์ประกอบ DOM | ภาพ, knob, track, piece | ภาพ, ปุ่ม Confirm |
| การกระทำ | ลาก knob 24 สเต็ป + jitter | คลิก 2 จุด (ห่าง 250–450 ms) + Confirm |
| ตรวจผล | main page (+ iframe ถ้า Symbol) | เหมือนกัน (ตรวจ iframe + main page ทั้งคู่เมื่ออยู่ใน iframe) |
| เวลา solver (ไฟล์ benchmark) | avg 22.4 ms (Python), 231 ms server calc (ผ่าน API จริง) | avg 111.1 ms (Python) |

หมายเหตุ: ตัวเลข "ลาก 500–800 ms / คลิก 250–450 ms" ในเอกสารเก่าเป็นการประมาณ ไม่ได้วัด; ค่าจริงของการลาก = 24 สเต็ป × 12–22 ms + 60 ms (≈ 350–590 ms ตามโค้ด)

## 7. ข้อสังเกต/ช่องว่าง (ยังไม่ยืนยันจากโค้ด)

- สถานะผ่าน/ล้มของชุดเทสต์ปัจจุบันทั้งหมด (ไม่ได้รันในรอบนี้)
- ว่าชุดทดสอบ xUnit ที่ขึ้นกับ `extracted_sources/` มีสำเนาที่ใดนอกเครื่องนี้
- วิธีเก็บ ground truth เพื่อวัดความแม่นยำของ Symbol/Slide (ไม่พบในรีโป)
- ตัวเลือก CLI ของ `python/slide/main.py`
- การรัน flow "แอปจริง + mock + backend จริง" ครบวงจร
