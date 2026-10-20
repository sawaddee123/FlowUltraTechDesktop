import { ArrowRight, Download } from 'lucide-react'
import { useApp } from '@/lib/store'
import { Badge, Button } from '@/components/ui/base'
import { Sheet } from '@/components/ui/overlay'

const notes = [
  'ทักทายผู้ชมแบบรวมชื่อเป็นชุด ลดโอกาสโดน rate limit',
  'Inactivity modal กดยืนยันให้อัตโนมัติ',
  'แก้ตัวนับเพชรนับซ้ำในมอนิเตอร์แชท',
]

export function UpdateSheet() {
  const { open, mandatory } = useApp((s) => s.update)
  const setUpdate = useApp((s) => s.setUpdate)
  const close = () => setUpdate({ open: false, mandatory: false })
  return (
    <Sheet
      open={open} onClose={close} dismissible={!mandatory} width={420}
      title={mandatory ? 'ต้องอัปเดตก่อนใช้งานต่อ' : 'มีเวอร์ชันใหม่'}
      footer={
        <div className="flex items-center justify-end gap-2">
          {!mandatory && <Button variant="ghost" onClick={close}>ไว้ภายหลัง</Button>}
          <Button variant="primary" icon={<Download className="size-4" />} onClick={close}>ดาวน์โหลดและอัปเดต</Button>
        </div>
      }
    >
      <div className="flex items-center gap-3 py-2">
        <span className="rounded-ctl bg-surface-2 px-3 py-1.5 text-sm tnum text-muted">1.7.0</span>
        <ArrowRight className="size-4 text-subtle" aria-hidden />
        <span className="rounded-ctl bg-primary-soft px-3 py-1.5 text-sm font-semibold tnum text-primary">1.8.0</span>
        {mandatory && <Badge tone="bad" dot>บังคับ</Badge>}
      </div>
      <h3 className="mt-5 text-[13px] font-semibold uppercase tracking-wide text-subtle">มีอะไรใหม่</h3>
      <ul className="mt-2 flex flex-col gap-2.5">
        {notes.map((n) => (
          <li key={n} className="flex gap-2.5 text-sm text-muted"><span className="mt-2 size-1 shrink-0 rounded-full bg-primary" />{n}</li>
        ))}
      </ul>
      {mandatory && <p className="mt-6 text-sm text-bad">เวอร์ชันนี้ต่ำกว่าที่เซิร์ฟเวอร์รองรับ ปิดหน้านี้ = ปิดแอป</p>}
    </Sheet>
  )
}
