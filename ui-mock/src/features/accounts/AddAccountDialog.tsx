import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Check } from 'lucide-react'
import { useApp } from '@/lib/store'
import { displayName } from '@/lib/format'
import { Button, Spinner } from '@/components/ui/base'
import { Dialog } from '@/components/ui/overlay'

const AUTO = ['ตั้งชื่อบัญชีจากโปรไฟล์ TikTok', 'จัดโฟลเดอร์และพอร์ตให้เอง', 'เลือก Proxy ที่ว่างให้อัตโนมัติ', 'เปิด AI แก้ Captcha ไว้ให้แล้ว']

/** เพิ่มบัญชีขั้นตอนเดียว: เปิดหน้าล็อกอิน → รอ → เสร็จ */
export function AddAccountDialog() {
  const nav = useNavigate()
  const open = useApp((s) => s.addOpen)
  const setOpen = useApp((s) => s.setAddOpen)
  const add = useApp((s) => s.addInstance)
  const openLogin = useApp((s) => s.openLogin)
  const remove = useApp((s) => s.removeInstance)
  const start = useApp((s) => s.startInstance)
  const [id, setId] = useState<string | null>(null)
  const inst = useApp((s) => s.instances.find((i) => i.id === id))
  const done = !!inst?.loggedIn

  useEffect(() => { if (!open) setId(null) }, [open])

  const begin = () => {
    const nid = add()
    if (!nid) return
    setId(nid)
    openLogin(nid)
  }
  const cancel = () => {
    if (id && !done) remove(id)
    setId(null)
  }
  const close = () => { cancel(); setOpen(false) }

  return (
    <Dialog open={open} onClose={close} label="เพิ่มบัญชี TikTok">
      {!id ? (
        <div className="flex flex-col gap-6">
          <div>
            <h2 className="text-2xl font-semibold">เพิ่มบัญชี TikTok</h2>
            <p className="mt-2 text-[15px] text-muted">กดปุ่มด้านล่าง แล้วล็อกอินในหน้าต่างที่เปิดขึ้น ที่เหลือเราจัดการให้</p>
          </div>
          <ul className="flex flex-col gap-3 text-[15px]">
            {AUTO.map((t) => <li key={t} className="flex items-center gap-3"><Check className="size-5 text-ok" strokeWidth={2.4} aria-hidden />{t}</li>)}
          </ul>
          <div className="flex flex-wrap gap-2.5">
            <Button variant="primary" className="h-13 flex-[1_1_220px] text-base" onClick={begin}>เปิดหน้าล็อกอิน TikTok</Button>
            <Button className="h-13 px-6 text-[15px]" onClick={close}>ยกเลิก</Button>
          </div>
        </div>
      ) : !done || !inst ? (
        <div role="status" className="flex flex-col items-center gap-5 py-4 text-center">
          <Spinner className="size-11 text-primary" />
          <div>
            <h2 className="text-xl font-semibold">รอคุณล็อกอิน…</h2>
            <p className="mt-1.5 text-[15px] text-muted">ล็อกอินในหน้าต่าง TikTok LIVE Studio ที่เพิ่งเปิดขึ้น<br />หน้านี้จะไปต่อเองเมื่อเสร็จ</p>
          </div>
          <Button className="h-11 px-5" onClick={cancel}>ยกเลิก</Button>
        </div>
      ) : (
        <div className="flex flex-col items-center gap-5 py-2 text-center">
          <span className="grid size-14 place-items-center rounded-full bg-ok-soft"><Check className="size-7 text-ok" strokeWidth={2.6} aria-hidden /></span>
          <div>
            <h2 className="text-[22px] font-semibold">เพิ่ม {displayName(inst)} แล้ว</h2>
            <p className="mt-1.5 text-[15px] text-muted">@{inst.handle} · พร้อมไลฟ์</p>
          </div>
          <div className="flex w-full flex-wrap gap-2.5">
            <Button variant="primary" className="h-13 flex-[1_1_200px] text-base" onClick={() => { start(inst.id); setOpen(false); nav('/accounts') }}>เริ่มไลฟ์เลย</Button>
            <Button className="h-13 flex-[1_1_160px] text-[15px]" onClick={() => setId(null)}>เพิ่มอีกบัญชี</Button>
          </div>
        </div>
      )}
    </Dialog>
  )
}
