import { useEffect, useState } from 'react'
import { useApp } from '@/lib/store'
import { Badge, Button, Meter } from '@/components/ui/base'
import { Dialog } from '@/components/ui/overlay'

const notes = [
  'ทักทายผู้ชมแบบรวมชื่อเป็นชุด ลดโอกาสโดน rate limit',
  'Inactivity modal กดยืนยันให้อัตโนมัติ',
  'แก้ตัวนับเพชรนับซ้ำในมอนิเตอร์แชท',
]

/** อัปเดต: กดครั้งเดียว ดาวน์โหลดเบื้องหลัง แล้วรีสตาร์ตเมื่อพร้อม */
export function UpdateSheet() {
  const { open, mandatory } = useApp((s) => s.update)
  const setUpdate = useApp((s) => s.setUpdate)
  const toast = useApp((s) => s.toast)
  const [pct, setPct] = useState<number | null>(null)
  const ready = pct === 100
  const close = () => setUpdate({ open: false, mandatory: false })

  useEffect(() => { if (!open) setPct(null) }, [open])
  useEffect(() => {
    if (pct === null || ready) return
    // mock: ความคืบหน้าปลอม
    const t = window.setTimeout(() => setPct((p) => Math.min(100, (p ?? 0) + 7)), 200)
    return () => window.clearTimeout(t)
  }, [pct, ready])

  return (
    <Dialog open={open} onClose={close} dismissible={!mandatory} label="อัปเดตโปรแกรม" width={500}>
      <div className="flex flex-col gap-5">
        <header>
          <div className="flex items-center gap-2.5">
            <h2 className="text-[22px] font-semibold">{mandatory ? 'ต้องอัปเดตก่อนใช้งานต่อ' : 'มีเวอร์ชันใหม่'}</h2>
            {mandatory && <Badge tone="bad">จำเป็น</Badge>}
          </div>
          <p className="mt-1 text-sm text-muted tnum">1.7.0 → 1.8.0</p>
        </header>
        <div>
          <h3 className="text-sm font-semibold text-muted">มีอะไรใหม่</h3>
          <ul className="mt-2 flex list-disc flex-col gap-2 pl-5 text-[15px]">{notes.map((n) => <li key={n}>{n}</li>)}</ul>
        </div>
        {pct !== null && !ready && (
          <div role="status" className="flex flex-col gap-2">
            <div className="flex justify-between text-sm"><span>กำลังดาวน์โหลด…</span><span className="tnum">{pct}%</span></div>
            <Meter value={pct} max={100} />
            <span className="text-[13px] text-muted">ไลฟ์ที่เปิดอยู่ยังทำงานต่อได้ระหว่างนี้</span>
          </div>
        )}
        {ready && <p role="status" className="rounded-ctl bg-ok-soft px-3.5 py-3 text-sm text-ok">ดาวน์โหลดเสร็จแล้ว ติดตั้งตอนรีสตาร์ต ใช้เวลาไม่กี่วินาที</p>}
        <div className="flex flex-wrap gap-2.5">
          {pct === null && <Button variant="primary" className="h-13 flex-[1_1_200px] text-base" onClick={() => setPct(0)}>อัปเดตเลย</Button>}
          {ready && <Button variant="primary" className="h-13 flex-[1_1_200px] text-base" onClick={() => { close(); toast({ tone: 'ok', title: 'รีสตาร์ตแล้ว', detail: 'เวอร์ชัน 1.8.0' }) }}>รีสตาร์ตตอนนี้</Button>}
          {!mandatory && <Button className="h-13 flex-[1_1_140px] text-[15px]" onClick={close}>{ready ? 'ติดตั้งตอนเปิดครั้งหน้า' : 'ไว้ทีหลัง'}</Button>}
        </div>
        {mandatory && <p className="text-[13px] text-muted">เวอร์ชันนี้เก่าเกินกว่าที่เซิร์ฟเวอร์รองรับ ต้องอัปเดตก่อนใช้งานต่อ</p>}
      </div>
    </Dialog>
  )
}
