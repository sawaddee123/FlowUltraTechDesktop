# Studio Live · FlowUltraTech — UI Mock

Mock แบบคลิกได้ของ UI ใหม่ทั้งแอป (Vite + React + TypeScript + Tailwind v4) ข้อมูลทั้งหมดจำลอง ไม่มี backend

รัน: `npm run dev` → http://127.0.0.1:5174 · ตรวจ: `npm run typecheck` · build: `npm run build`

## Design context

- **ผู้ใช้:** ผู้ทำไลฟ์ขายของบน TikTok ที่รัน Studio หลายบัญชีพร้อมกันบนเครื่องเดียว นั่งหน้าจอนาน ต้องกวาดสายตาดูสถานะหลายบัญชี และตอบสนอง Captcha ให้ทัน
- **งานหลัก:** ดูว่าบัญชีไหนปกติ/ผิดปกติ → เริ่ม-หยุด → จัดการ Captcha → ตั้งตอบแชทอัตโนมัติ → ตั้ง Overlay/Proxy
- **บุคลิก:** ห้องควบคุมที่นิ่งและสงบ (calm control room) แน่นแต่ไม่รก ใช้สีเฉพาะจุดที่ต้องลงมือ
- **จุดจำ:** Master–Detail ที่ปุ่มและรายการไม่ขยับ, ธีมกรมท่าตามโลโก้ Studio Live, สลับ Dark/Light ได้
- **ตัวอักษร:** Anuphan (ไทย+ละติน, variable) · JetBrains Mono เฉพาะข้อมูลดิบ (log, HWND, URL, JSON)

## ธีมและโทเคน (ทุกอย่างอยู่ใน `src/styles.css`)

ห้ามใช้สีฮาร์ดโค้ดในคอมโพเนนต์ ใช้ utility ที่ผูกกับโทเคนเท่านั้น

| กลุ่ม | utility | ใช้กับ |
| :--- | :--- | :--- |
| พื้น | `bg-bg` → `bg-surface` → `bg-surface-2` → `bg-surface-3` | canvas → พื้นผิวหลัก → hover/ช่องกรอก → active |
| ข้อความ | `text-fg` · `text-muted` · `text-subtle` | หลัก · รอง · เมตาดาต้า |
| เส้น | `bg-line` / `hairline` | ใช้น้อยที่สุด (ช่องว่างก่อน เงาที่สองเส้นสุดท้าย) |
| เน้น | `bg-primary` `text-on-primary` `bg-primary-soft` `text-primary` | ปุ่มหลัก 1 ปุ่ม/มุมมอง และ state active |
| สถานะ | `ok` `warn` `bad` (+ `-soft`) | สำเร็จ · เตือน · ผิดพลาด |
| มุม | `rounded-ctl` (ปุ่ม/ช่องกรอก) · `rounded-card` (พื้นผิว) · `rounded-full` (pill) | |
| ลอย | `shadow-float` | เมนู ลิ้นชัก toast |

กฎ: 60-30-10 · มี focal point เดียวต่อมุมมอง · ไม่ซ้อนกรอบในกรอบ · ไม่ใช้ gradient/glow ตกแต่ง · ไม่ใช้ emoji ในระบบ (ใช้ lucide-react, stroke เดียวกัน, `aria-hidden` ถ้าตกแต่ง) · สถานะใช้ `StatusDot`/`Badge` ไม่ใช่ emoji

## คอมโพเนนต์ที่ใช้ร่วม (`src/components/ui/`)

- `base.tsx`: `Button` (variant primary|secondary|ghost|danger, size sm|md, `loading`, `icon`) · `IconButton` (ต้องมี `label`) · `Badge` · `StatusDot` · `Switch` · `Segmented` · `Skeleton` · `Spinner` · `Avatar` · `Meter` · `Kbd` · `CopyButton` · `EmptyState` · `Stat` · `toneClass`
- `form.tsx`: `Field` (label + hint ใน tooltip + error) · `Input` · `NumberInput` · `Textarea` · `Select` · `Slider` · `ColorInput` · `ToggleRow`
- `overlay.tsx`: `Menu` · `Sheet` (ลิ้นชักข้าง แทน modal) · `Confirm` (เฉพาะลบถาวร)
- `layout.tsx`: `Tabs` · `PageHeader` · `Section` · `Surface` · `DataTable` + `tdc`

## กติกา UX

1. **Spatial continuity:** ปุ่มเปิด/ปิดอยู่ที่เดิมเสมอ ข้อมูลที่ต้องดูคู่กันอยู่ข้างกัน (split pane) ไม่เปลี่ยนหน้าทั้งจอเพื่อดูรายละเอียด
2. **Async feedback:** ทุกปุ่มที่ใช้เวลาต้อง `loading` (ล็อกความกว้าง) + มี skeleton ที่ขนาดตรงของจริง ห้ามมี spinner เต็มจอ ห้ามค้างนิ่ง; timeout/ล้มเหลวต้องมีปุ่มลองใหม่
3. **Micro-copy:** ไม่ใช้ "กรุณา/โปรด/ท่านสามารถ" ปุ่มขึ้นต้นด้วยกริยา (`บันทึก`, `ทดสอบ`) คำอธิบายยาวซ่อนใน tooltip (`Field hint`) หรือ `<details>`
4. **Progressive disclosure:** ตัวเลือกพื้นฐานก่อน ขั้นสูงพับไว้
5. **Empty state สอนใช้งาน:** บอกว่าต้องทำอะไรต่อ ไม่ใช่แค่ "ไม่มีข้อมูล"
6. **Accessibility:** ปุ่มไอคอนมี `aria-label`, ใช้ role ที่ถูก (tablist/switch/radiogroup), focus ring จาก `:focus-visible`, สีไม่ใช่ตัวบอกสถานะอย่างเดียว

## ประสิทธิภาพ

- ทุกหน้าและทุกแท็บเป็น `lazy` chunk (ดู `App.tsx`, `tabs/registry.tsx`)
- แถวรายการใช้ `React.memo` + `cv-auto` (content-visibility) · ลิสต์สดจำกัด ≤ 200 แถว
- อนิเมชันใช้ `transform`/`opacity` เท่านั้น (ยกเว้น `Meter` ที่ใช้ `scaleX`) · เคารพ `prefers-reduced-motion`
- state ที่เปลี่ยนทุกวินาที (นับถอยหลัง, ข่าวสด) ต้องอยู่ใน component ลูกเล็กๆ ไม่ดัน state ขึ้น store ทั้งแอป
- ฟอนต์โหลดตาม unicode-range; ภาพโลโก้เป็น webp ล็อกสัดส่วนแล้ว

## โครงสร้าง

```
src/
  styles.css           โทเคน + base
  lib/                 types, mock data, store (zustand), simulation, format
  components/ui/       คอมโพเนนต์ร่วม
  components/shell/    Sidebar, Topbar, StatusBar, Toasts, CommandPalette, NotificationPanel
  features/            accounts · captcha · proxy · rules · settings · auth · update · design
```

เส้นทาง: `#/accounts/:id?/:tab?` · `#/captcha` · `#/proxy` · `#/rules` · `#/settings/:section?` · `#/design` · `#/login`

## สิ่งที่ mock จำลอง

เมนูแฟลสก์บนแถบบน = จำลอง Captcha (Jigsaw/Symbol), อัปเดตใหม่/บังคับ, แจ้งเตือน · `Ctrl K` = พาเลตต์คำสั่ง · รหัสผ่าน `wrong` ที่หน้า Login = จำลองผิดพลาด
