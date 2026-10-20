# 02. การเปิดหลายอินสแตนซ์ และการควบคุมหน้าต่าง

อัปเดตล่าสุด: 2026-10-20 · เวอร์ชันแอป: 1.7.0 (อ่านจาก [FlowUltraTech.App.csproj](../src/FlowUltraTech.App/FlowUltraTech.App.csproj))

ครอบคลุม: การเปิดหลายอินสแตนซ์พร้อมกัน, การแยกโปรไฟล์, การหา Path ของ Studio, Process Tree, การซ่อน/แสดงหน้าต่างด้วย Win32, การลดทรัพยากร และข้อจำกัดที่พบจากโค้ด

เอกสารที่เกี่ยวข้อง: [01 ภาพรวม](01_architecture_overview.md) · [03 Network และ Proxy](03_network_and_proxy.md) · [04 UI](04_ui_and_user_guide.md)

---

## 1. แนวคิดการเปิดหลายอินสแตนซ์

1 `StudioInstanceProfile` ([StudioInstanceProfile.cs](../src/FlowUltraTech.Core/Models/StudioInstanceProfile.cs)) = 1 โปรเซสต้นทาง TikTok LIVE Studio = 1 พอร์ต CDP = 1 โฟลเดอร์โปรไฟล์

| ฟิลด์ | ความหมาย | ค่าเริ่มต้น |
|---|---|---|
| `Id` | GUID ใช้ผูก Proxy Bridge และ Overlay | สุ่มใหม่ |
| `Name` | ชื่อที่แสดง | `Account 1` |
| `ProfileFolderName` (`SandboxBoxName` เป็น Alias) | ชื่อโฟลเดอร์โปรไฟล์ / ใช้ตั้งชื่อ Log และ Symlink | `Acc01` |
| `CdpPort` | พอร์ต `--remote-debugging-port` | 9221 |
| `UserDataDir` | Path โปรไฟล์แบบกำหนดเอง (ว่าง = ใช้ค่ามาตรฐาน) | ว่าง |
| `LaunchMode` | `DirectProfile = 1` หรือ `DefaultProfile = 2` ([Enums.cs](../src/FlowUltraTech.Core/Models/Enums.cs)) | `DirectProfile` |
| `CustomChromiumFlags` | Flag เพิ่มเติมรายอินสแตนซ์ | ว่าง |
| `Status` / `StatusMessage` / `ProcessId` / `StartedAt` / `IsWindowHidden` | สถานะขณะรัน (ไม่ใช่ค่าถาวร) | `Stopped` |
| `ActiveLeaseId` | Slot Lease ที่ถือครอง | null |

`InstanceStatus`: `Stopped=0`, `Starting=1`, `Running=2`, `Error=3`

### โหมดโปรไฟล์

| โหมด | `--user-data-dir` | โฟลเดอร์ข้อมูล | ใช้เมื่อ |
|---|---|---|---|
| `DirectProfile` | ใส่ | `UserDataDir` ถ้ากำหนด, ไม่เช่นนั้น `%LOCALAPPDATA%\TikTokStudioProfiles\<ProfileFolderName>` (ถ้าชื่อว่างใช้ `SandboxBoxName`, ถ้ายังว่างใช้ `Acc01`) | โหมดหลัก แยก Cookie/Session ต่อบัญชี |
| `DefaultProfile` | ไม่ใส่ | `%APPDATA%\TikTok LIVE Studio` | ใช้บัญชีหลักของ Studio ที่ติดตั้งในเครื่อง |

Path ถูกคำนวณที่ `ProfileManagerService.ResolveProfileDirectoryStatic` ([ProfileManagerService.cs](../src/FlowUltraTech.Core/Services/ProfileManagerService.cs))

สลับโหมดได้จากปุ่มโหมดในแท็บ "เซสชันบัญชี" (`InstanceCardViewModel.SwitchMode`) ซึ่งเปลี่ยนค่า `LaunchMode` ของโปรไฟล์ทันที

### การแยกโปรเซสด้วย Symlink ต่อโปรไฟล์

ในโหมด `DirectProfile` `ProcessLauncher` พยายามสร้าง Symbolic Link `TikTok LIVE Studio_<folderTag>.exe` (ในโฟลเดอร์เดียวกับ exe จริง) ชี้ไปที่ `TikTok LIVE Studio.exe` แล้วเปิดผ่านลิงก์นั้น โดย `folderTag` คือ `ProfileFolderName` (ถ้าว่างใช้ `Name`)

ผลที่โค้ดใช้ประโยชน์:

- ชื่อโปรเซสแยกต่อโปรไฟล์ → `KillInstance`/`FindProcessId` ค้นหาตามชื่อ `TikTok LIVE Studio_<folderTag>` ได้
- Rule ของ Proxifier ระบุชื่อ exe ต่อโปรไฟล์ได้ ([03](03_network_and_proxy.md))
- ถ้าสร้าง Symlink ไม่ได้ (เช่น ไม่มีสิทธิ์) โค้ดจะ **เงียบ** และ fallback ไปเปิด exe จริงชื่อเดิม ซึ่งกระทบการปิดอินสแตนซ์ (ดูหัวข้อข้อจำกัด)

เหตุผลที่แท้จริงของการแยกชื่อ exe (เช่น เลี่ยงกลไก Single-instance ของ Studio) ไม่ได้ระบุในโค้ด จึง **ยังไม่ยืนยันจากโค้ด**

---

## 2. การหา Path ของ TikTok LIVE Studio

[TikTokStudioPathResolver.ResolveValidStudioPath](../src/FlowUltraTech.Core/Services/TikTokStudioPathResolver.cs) เรียงลำดับดังนี้

1. ถ้า `TikTokStudioExePath` ที่ตั้งไว้มีอยู่จริง และไม่ลงท้ายด้วย `Launcher.exe` → ใช้ตามนั้น
2. ถ้าชี้ไป `Launcher.exe` → หา exe ตัวจริงในโฟลเดอร์เดียวกัน (`FindRealStudioExeInDir`)
3. ลอง `C:\Program Files\TikTok LIVE Studio` (`FindRealStudioExeInDir` → `TikTok LIVE Studio.exe` → `TikTok LIVE Studio Launcher.exe`)
4. ลอง `%LOCALAPPDATA%\Programs\TikTok LIVE Studio\TikTok LIVE Studio.exe`
5. ไม่พบเลย → คืนค่าที่ตั้งไว้เดิม (หรือ Path มาตรฐาน) แล้ว `ProcessLauncher` โยน `FileNotFoundException`

`FindRealStudioExeInDir`: อ่าน `launcher_config.json` ค่า `cur_path` ก่อน แล้วหาโฟลเดอร์เวอร์ชันย่อย (เรียงชื่อมากไปน้อย) ที่มี `TikTok LIVE Studio.exe`

ค่าเริ่มต้นใน `AppSettings` คือ `C:\Program Files\TikTok LIVE Studio\1.31.0\TikTok LIVE Studio.exe` และ `ConfigStorageService.CreateDefaultSettings` ตรวจ 3 Path ตัวอย่างเมื่อสร้าง Config ครั้งแรก

---

## 3. ลำดับการเปิดโปรเซส ([ProcessLauncher.cs](../src/FlowUltraTech.Core/Services/ProcessLauncher.cs))

`InstanceOrchestrator.StartInstanceAsync` เรียก `BindProtocolToProfile` แล้ว `ProcessLauncher.LaunchInstanceAsync` ตามลำดับ:

1. **ตรวจว่าเปิดอยู่แล้วหรือไม่** (`IsInstanceRunning`): ถ้าพอร์ต CDP ตอบ (TCP connect ภายใน 300 ms) หรือ PID เดิมยังอยู่
   - มีหน้าต่าง UI จริง → ตั้ง `Running`, ดึงหน้าต่างขึ้น แล้วคืน Process เดิม
   - ไม่มีหน้าต่าง = **Zombie** → `KillInstance` แล้วรอ 600 ms และเปิดใหม่
2. **หา exe** ด้วย Path Resolver (ไม่พบ = Exception)
3. **ลบไฟล์ล็อก Chromium** ที่ตกค้าง `SingletonLock`, `SingletonSocket`, `SingletonCookie` ในโฟลเดอร์โปรไฟล์
4. **ประกอบ Flag** (ตารางด้านล่าง)
5. **ผูก Proxy** ผ่าน `LocalProxyBridgeService` (ดู [03](03_network_and_proxy.md)); ถ้าไม่มี Proxy จะหยุด Bridge ของอินสแตนซ์นั้น
6. **เลือก exe ที่จะเปิด** (DirectProfile: สร้าง/ใช้ Symlink ต่อโปรไฟล์) แล้ว `Process.Start`
   - ถ้าเจอ Win32 Error 740 (ต้องยกระดับสิทธิ์) จะลองใหม่ด้วย `UseShellExecute=true, Verb="runas"`
7. **รอพอร์ต CDP** ทุก 500 ms สูงสุด 30 รอบ (15 วินาที) โดยตรวจด้วย `netstat -ano -p tcp`
   - ไม่พบพอร์ตและโปรเซสจบไปแล้ว → `Error` (`Process exited with code N`)
   - ไม่พบพอร์ตแต่โปรเซสยังอยู่ → เตือนใน Log แล้วไปต่อ
8. **กำหนด `ProcessId`**: ถ้าโปรเซสที่สตาร์ทยังอยู่ใช้ PID นั้น มิฉะนั้นไล่หา Root จาก PID ที่ฟังพอร์ต (`ProcessTreeHelper`)
9. ตั้ง `Status = Running`, `StatusMessage = "Running on port N"`

จากนั้น Orchestrator เริ่ม Exit Watcher และ Task รอหน้าต่าง (หัวข้อ 6)

### Argument ที่ใช้เปิด

รูปแบบเต็ม (DirectProfile): `--user-data-dir="<โปรไฟล์>" --remote-debugging-port=<พอร์ต> <proxyFlags> <baseFlags> <CustomChromiumFlags>`
(DefaultProfile ไม่มี `--user-data-dir`)

| ส่วน | ค่าจริงในโค้ด |
|---|---|
| Anti-throttling (ใส่เสมอ) | `--disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows --disable-ipc-flooding-protection` |
| `baseFlags` โหมดปกติ | Anti-throttling + `AppSettings.DefaultChromiumFlags` |
| `baseFlags` โหมด Ultra-Light | `--disable-gpu --disable-gpu-compositing --disable-software-rasterizer --disable-d3d11 --disable-accelerated-2d-canvas --disable-gpu-rasterization --no-sandbox` + Anti-throttling |
| `proxyFlags` | `--proxy-server="http://127.0.0.1:<พอร์ต Bridge>" --proxy-bypass-list="<-loopback>"` (เฉพาะเมื่อมี Proxy) |
| `WorkingDirectory` | โฟลเดอร์ของ exe จริง |

Flag Anti-throttling มีเพื่อให้ Studio ทำงานต่อเมื่อหน้าต่างถูกซ่อน/บัง (ตามชื่อ Flag)

---

## 4. Process Tree และการปิดอินสแตนซ์

### ProcessTreeHelper ([ProcessTreeHelper.cs](../src/FlowUltraTech.Core/Services/ProcessTreeHelper.cs))

- `CreateSnapshot()` ใช้ `CreateToolhelp32Snapshot` เก็บ Parent/Children/ชื่อ exe ของทุกโปรเซส
- `GetProcessTree(startPid)`: ไต่ขึ้นหา Root ตราบที่ **ชื่อ exe ของ Parent มีคำว่า "TikTok"** แล้วเดินลงเก็บลูกหลานทั้งหมด (BFS)
- ไม่รวม PID ของแอปเอง (`Environment.ProcessId`) ไม่ว่ากรณีใด

### ProcessLauncher.KillInstance

| ขั้น | การทำงาน |
|---|---|
| 0 | หยุด Proxy Bridge ของอินสแตนซ์ |
| 1 | หา PID ที่ฟังพอร์ต CDP → เก็บ Tree |
| 2 | เก็บ Tree จาก `ProcessId` ที่บันทึกไว้ |
| 2.1 | หาโปรเซสชื่อ `TikTok LIVE Studio_<folderTag>` → เก็บ Tree |
| 3 | ถ้ายังไม่พบอะไร → `FindProcessId` (พอร์ต → ชื่อ Symlink → ชื่อ `TikTok LIVE Studio` ถ้ามีตัวเดียวใช้ตัวนั้น มิฉะนั้นเลือกตัวที่เริ่มล่าสุด) |
| 4 | ถ้ายังไม่พบ → เก็บทุกโปรเซสชื่อ `TikTok LIVE Studio` |
| 5-6 | `ForceKillProcessTree` ที่ Root และ PID ที่เหลือ |
| 7 | **เก็บกวาดสุดท้าย:** ปิดทุกโปรเซสที่ชื่อ `TikTok LIVE Studio` ที่ยังเหลือ |

`ForceKillProcessTree` ลอง 3 ชั้น: `Process.Kill(entireProcessTree: true)` → `taskkill /F /T /PID` → `taskkill` ยกระดับด้วย `runas`

หลังปิดตั้ง `Status=Stopped`, `ProcessId=null`, `IsWindowHidden=false`

### ปิดทั้งหมด

`ProcessLauncher.KillAllStudioProcesses()` ปิดทุกโปรเซสที่ชื่อขึ้นต้น `TikTok LIVE Studio` (ไม่เฉพาะที่แอปเปิด) แล้วรัน `taskkill /F /IM "TikTok LIVE Studio*.exe" /T` ซ้ำ ถูกเรียกจาก: ปุ่ม "ปิดทุกจอ", `MainWindow.Closing`, `App.OnExit`, `AppDomain.ProcessExit`

### การตรวจพอร์ต

`GetPidListeningOnPort` เรียก `netstat -ano -p tcp` ใหม่ทุกครั้ง (Timeout รอ 1.5 วินาที) แล้วหาบรรทัด `LISTENING` ที่มี `:<พอร์ต> ` — คืน `null` ถ้า PID ตรงกับแอปเอง

---

## 5. Exit Watcher และ Login

### Exit Watcher ([InstanceOrchestrator.cs](../src/FlowUltraTech.Core/Services/InstanceOrchestrator.cs))

หลังเปิดสำเร็จ จะรอ 2.5 วินาที แล้วตรวจทุก 1.5 วินาทีว่า `IsInstanceRunning` ยังเป็นจริงหรือไม่ ถ้าไม่ จะตั้ง `Stopped` (ข้อความ "Stopped (โปรแกรมถูกปิด)"), ล้างแคชหน้าต่างของพอร์ต และยิง Event `InstanceExited` ซึ่งการ์ดรับไปหยุด timer/Watcher และล้างรายการหน้าต่าง

### การล็อกอิน

- **Login Watcher** (`StartLoginWatcher`): ใช้ `ProfileManagerService.WatchProfile` เฝ้า `<โปรไฟล์>\TTStore` (`FileSystemWatcher` กรองชื่อไฟล์ที่มี `services`/`login`/`avatar`, Debounce 300 ms + Polling สำรองทุก 1.5 วินาที) เมื่ออ่าน `services.json` (`AccountService.state.accountList[0]`) ได้ `userId` + (`displayId` หรือ `nickname`) ถือว่าล็อกอินแล้ว
- `OpenStudioForLoginAsync`: เปิด Studio **แบบเห็นหน้าต่างปกติ** (ใช้ `AppSettings` ชั่วคราวที่ `AutoHideOnLaunch=false`) เมื่อตรวจพบการล็อกอิน (`autoClose: true`) จะรอ 1.5 วินาทีให้ Chromium เขียนไฟล์ แล้ว `StopAsync` เอง
- Watchdog 25 วินาที: ถ้าสถานะยังเป็น `Starting` จะ `StopAsync` และรีเซ็ตเป็น `Stopped`
- `BindProtocolToProfile`: เขียน `HKCU\Software\Classes\live-studio-app` ให้ชี้ไปยังโปรไฟล์ที่กำลังใช้ (DirectProfile: `"<studio.exe>" --user-data-dir="<โปรไฟล์>" "%1"`; DefaultProfile: `TikTok LIVE Studio Launcher.exe` หรือ exe จริง) เขียนใหม่เมื่อค่าเปลี่ยนเท่านั้น ถูกเรียกก่อนเริ่ม/ล็อกอินทุกครั้ง ทำให้ Protocol ชี้ไปโปรไฟล์ล่าสุดที่เปิดเสมอ (ค่าเดียวทั้งเครื่อง)
- `ForwardLoginCallback`: ผู้ใช้คัดลอกลิงก์ `live-studio-app://...` ไว้ใน Clipboard แล้วแอปเปิด `studio.exe --user-data-dir=<โปรไฟล์> "<url>"`
- `CloneSessionFromDefaultProfile`: คัดลอก `Network\*Cookies*`, `Local Storage`, `Session Storage`, `TTStore` จาก `%APPDATA%\TikTok LIVE Studio` ไปโปรไฟล์ปลายทาง (ปุ่ม "คัดลอก Session จากบัญชีหลัก")

---

## 6. การค้นหาและควบคุมหน้าต่าง ([WindowController.cs](../src/FlowUltraTech.Core/Services/WindowController.cs), [Win32.cs](../src/FlowUltraTech.Core/Native/Win32.cs))

### หน้าต่างแบบไหนนับเป็น "หน้าต่าง Studio จริง" (`IsRealStudioWindow`)

- Class ต้องเป็น `Chrome_WidgetWin_1` เท่านั้น
- ต้องมี Title ไม่ว่าง และ **ไม่** มีคำ `Make Your Day`, `For You`, `tiktok.com`, `Chrome Legacy Window`, `Sandbox`
- ถ้าหน้าต่างถูกย่อ (Iconic) นับว่าจริง; มิฉะนั้นต้องกว้าง ≥ 400 และสูง ≥ 250

### การเลือกหน้าต่างหลัก

`FindWindowHandlesForPort(port, pid)`: รวบรวม PID ใน Process Tree (ถ้าไม่มี PID ใช้ `netstat`) → `EnumWindows` กรองด้วยเงื่อนไขข้างต้น → รวมกับแคชต่อพอร์ต → เรียงตาม `GetWindowScore` (Title มี TikTok+Studio/Live ได้ Score สูงสุด, หน้าต่างที่มองเห็น, พื้นที่ ≥ 500,000 px²) หน้าต่างแรก = Primary; หน้าต่างที่มี Owner เป็น Primary = Child Modal; ที่เหลือ = Auxiliary/Background

### ซ่อน / แสดง

| การกระทำ | Win32 ที่เรียก |
|---|---|
| ซ่อน (`SetSingleWindowVisibility(false)`, `SetAllWindowsVisibilityForInstance(false)`) | `PostMessage WM_SYSCOMMAND SC_MINIMIZE` + `ShowWindow SW_HIDE` + `ShowWindowAsync SW_HIDE` + `SetWindowPos ... SWP_HIDEWINDOW` |
| แสดง (`BringToFrontRockSolid`) | ถ้าย่ออยู่ `SC_RESTORE`/`SW_RESTORE` → `SW_SHOWNORMAL` + `SW_SHOW` → ถ้าพิกัดนอกจอ (< -5000) ย้ายไป (100,100) → `AttachThreadInput` + `BringWindowToTop` + `SetForegroundWindow` |
| แสดงผ่าน `SetWindowVisibilityForInstance(true)` | ดึง Primary (และ Modal) ขึ้นหน้าสุด และ **ซ่อน** หน้าต่างอื่นของโปรเซสนั้นที่ยังมองเห็น |

เส้นทางที่ UI ใช้จริง:

- ปุ่มซ่อน/แสดงของการ์ด (`ToggleVisibility`, `ShowWindow`, `HideWindow`) เรียก `WindowController.SetAllWindowsVisibilityForInstance` ตรง ซึ่งทำกับ **ทุกหน้าต่าง** ที่พบ
- ปุ่มระดับบน "ซ่อนทุกจอ/แสดงทุกจอ" วนเรียกเมธอดของการ์ด (เฉพาะสถานะ `Running`/`Starting`)
- แท็บ "หน้าต่าง (Handles)" แสดงรายการ `StudioWindowHandleInfo` (HWND, Title, Class, PID, ขนาด, Visible/Iconic, บทบาท) และควบคุมทีละหน้าต่างได้
- หลัง Start สำเร็จ การ์ดเรียก `Orchestrator.ShowWindow`

### Task รอหน้าต่างหลังเปิด (Orchestrator)

ทุก 500 ms สูงสุด 60 รอบ (30 วินาที) หา Handle ของพอร์ต → หา Title ที่มี `Studio`/`TikTok LIVE` → ถ้าขนาดใหญ่พอ (พื้นที่ ≥ 500,000 หรือ ≥ 900×550) หรือผ่านไป ≥ 15 วินาที จะเรียก `SetWindowVisibilityForInstance(true)` แล้วย้ำอีก 3 ครั้ง ห่าง 1 วินาที (กัน Splash/WebView แย่ง Focus); ถ้าครบ 30 วินาทีแล้วไม่พบ จะเตือนใน Log ให้กดปุ่มรูปตา

---

## 7. Pseudo-Headless และการลดทรัพยากร (ตามที่โค้ดทำจริง)

| กลไก | สถานะในโค้ด |
|---|---|
| ซ่อนหน้าต่าง (SW_HIDE + Minimize + SWP_HIDEWINDOW) | ใช้งานจริงผ่านปุ่มซ่อนในการ์ดและ "ซ่อนทุกจอ"; โค้ดไม่ได้ตัดการเชื่อมต่อ CDP เมื่อซ่อน (ผลต่อการเรนเดอร์/ทรัพยากรจริงยังไม่ได้วัด) |
| Flag Anti-throttling | ใส่ทุกครั้งที่เปิด |
| Ultra-Light (`LaunchUltraLightBackgroundAsync`, `InstanceOrchestrator.StartUltraLightLoginAsync`) | มี Logic ครบ (ปิด GPU/Compositing, `ProcessWindowStyle.Hidden`, ซ่อนทุก HWND ใน Tree) แต่ **ไม่มีจุดเรียกจาก UI**: `InstanceCardViewModel.StartUltraLightLoginAsync` เรียก `OpenStudioForLoginAsync` ซึ่งเปิดแบบเห็นหน้าต่าง |
| `AppSettings.AutoHideOnLaunch` (ช่องติ๊กใน Settings) | บันทึกค่าได้ แต่ไม่พบโค้ดฝั่ง Launcher/Orchestrator ที่อ่านค่านี้เพื่อซ่อนหน้าต่างหลังเปิด (`StartInstanceAsync` ตั้ง `IsWindowHidden=false` และแสดงหน้าต่างเสมอ) |
| `DefaultChromiumFlags` / `CustomChromiumFlags` | ใช้งานได้ในไฟล์ `config.json`; ไม่พบช่องกรอกใน UI |

ตัวเลขการประหยัดทรัพยากร (เช่น 50–70%) และข้อจำกัด NVENC ที่เอกสารเก่าเคยอ้างไม่สามารถยืนยันจากโค้ดได้

---

## 8. ข้อสังเกต/ช่องว่าง

1. **ปิดอินสแตนซ์หนึ่งอาจกระทบตัวอื่น:** ขั้น 7 ของ `KillInstance` ปิดทุกโปรเซสชื่อ `TikTok LIVE Studio` ที่เหลือ อินสแตนซ์ DirectProfile ที่เปิดผ่าน Symlink ใช้ชื่อ `TikTok LIVE Studio_<folder>` จึงไม่ถูกจับ แต่ถ้า Symlink สร้างไม่สำเร็จ (fallback เป็นชื่อเดิม) หรือเป็นโหมด `DefaultProfile` การปิดหนึ่งตัวอาจปิดทุกตัวที่ใช้ชื่อเดิม
2. `KillAllStudioProcesses` ปิดทุกโปรเซสที่ชื่อขึ้นต้น `TikTok LIVE Studio` ในเครื่อง รวมถึง Studio ที่ผู้ใช้เปิดเองนอกแอป และถูกเรียกตอนปิดแอปทุกครั้ง
3. `AddInstance` ตั้งพอร์ตเป็น `9220 + (จำนวนการ์ด+1)` และโฟลเดอร์เป็น `Acc{NN}` โดยไม่ตรวจชนกัน หากเคยลบบัญชีกลางลำดับอาจได้พอร์ต/โฟลเดอร์ซ้ำกับของเดิม
4. UI แสดง `Name` และ `CdpPort` แบบอ่านอย่างเดียว ไม่มีช่องแก้ไขชื่อ/พอร์ต/โฟลเดอร์/`UserDataDir` ต้องแก้ใน `config.json`
5. `GetPidListeningOnPort` สร้างโปรเซส `netstat` ใหม่ทุกครั้ง และถูกใช้ซ้ำทั้งใน Launcher, WindowController, การ์ด และ Telemetry จึงมีต้นทุนเมื่อมีหลายอินสแตนซ์ (ยังไม่ได้วัด)
6. สถานะ `IsWindowHidden` เป็นค่าที่แอปจำเอง หากผู้ใช้ซ่อน/ย่อหน้าต่างด้วยมือค่าอาจไม่ตรง (เมธอด `Orchestrator.RefreshStatus` ที่ซิงก์จากหน้าต่างจริงไม่ถูกเรียกจาก App; ตรวจพบเพียงการประกาศใน Interface/Implementation)
7. `Orchestrator.ToggleWindowVisibility`, `HideWindow`, `StopAllInstances`, `RefreshStatus` ไม่ถูกเรียกจากโค้ด App (UI ควบคุมผ่านการ์ด + `WindowController` โดยตรง)
8. `ProcessTreeHelper` ไต่หา Root โดยพึ่งชื่อ exe ที่มีคำว่า "TikTok"; ถ้า Studio เปลี่ยนชื่อโปรเซสการหา Root/Tree จะคลาดเคลื่อน
9. `InstanceOrchestrator` ตรวจ Title ด้วยข้อความ ("Studio", "TikTok LIVE", "Make Your Day" ฯลฯ) หากแอป Studio เปลี่ยน Title การตรวจหน้าต่างหลักอาจไม่พบ (ผู้ใช้ยังกดปุ่มรูปตาเพื่อบังคับแสดงได้)
10. ไม่ได้ตรวจสอบกลไก Single-instance ภายในของ Studio ว่าใช้ Mutex/File Lock ตามที่เอกสารเก่าเขียน — ยังไม่ยืนยันจากโค้ด (โค้ดฝั่ง Client ลบเฉพาะ `Singleton*` ของ Chromium)
