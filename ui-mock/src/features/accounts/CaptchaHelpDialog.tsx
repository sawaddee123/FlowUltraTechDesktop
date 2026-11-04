import { useEffect, useState } from 'react'
import { Check } from 'lucide-react'
import { useApp } from '@/lib/store'
import { displayName } from '@/lib/format'
import { Avatar, Button, Spinner } from '@/components/ui/base'
import { Dialog } from '@/components/ui/overlay'
import { CaptchaPreview } from '@/components/captcha/CaptchaPreview'

/** เปิดเมื่อ AI แก้ไม่ผ่าน — ให้ผู้ใช้แก้ในหน้าต่าง Studio หรือสั่ง AI ลองใหม่ */
export function CaptchaHelpDialog() {
  const id = useApp((s) => s.helpId)
  const setHelpId = useApp((s) => s.setHelpId)
  const inst = useApp((s) => s.instances.find((i) => i.id === id))
  const setHidden = useApp((s) => s.setWindowHidden)
  const solve = useApp((s) => s.solveCaptcha)
  const toast = useApp((s) => s.toast)
  const [retrying, setRetrying] = useState(false)
  const cap = inst?.captcha
  const close = () => setHelpId(null)

  useEffect(() => { if (!cap) setRetrying(false) }, [cap])

  if (!inst) return null
  const name = displayName(inst)

  return (
    <Dialog open={!!id} onClose={close} label={`ช่วยแก้ Captcha ${name}`} width={560}>
      {cap ? (
        <div className="flex flex-col gap-5">
          <header className="flex items-start gap-3.5">
            <Avatar name={name} hue={inst.hue} size={44} />
            <div className="min-w-0 flex-1">
              <h2 className="text-xl font-semibold">{name} ต้องให้คุณช่วย</h2>
              <p className="text-sm text-muted">{cap.aiFailed ? 'AI ลองแก้แล้วไม่ผ่าน' : 'ปิดแก้อัตโนมัติไว้'} · ไลฟ์ยังเปิดอยู่</p>
            </div>
            <span role="timer" aria-label="เวลาที่เหลือ" className="flex flex-col items-end">
              <span className={`text-[26px] font-bold leading-none tnum ${cap.countdown <= 20 ? 'text-bad' : 'text-warn'}`}>{cap.countdown}</span>
              <span className="text-xs text-muted">วินาที</span>
            </span>
          </header>
          <CaptchaPreview kind={cap.kind} seed={cap.sessionId} countdown={cap.countdown} className="w-full" />
          <p className="text-[15px]">เปิดหน้าต่าง Studio แล้วแก้ตามภาพ หน้านี้จะเปลี่ยนเองเมื่อแก้ผ่าน</p>
          {retrying && (
            <div role="status" className="flex items-center gap-3 rounded-ctl bg-primary-soft px-3.5 py-3 text-sm"><Spinner className="text-primary" />AI กำลังลองอีกครั้ง…</div>
          )}
          <div className="flex flex-wrap gap-2.5">
            <Button variant="primary" className="h-13 flex-[1_1_220px] text-base"
              onClick={() => { setHidden(inst.id, false); toast({ tone: 'info', title: 'เปิดหน้าต่าง Studio แล้ว' }) }}>เปิดหน้าต่าง Studio</Button>
            <Button className="h-13 flex-[1_1_160px] text-[15px]" disabled={retrying}
              onClick={() => { setRetrying(true); solve(inst.id, 'ai-manual') }}>ให้ AI ลองอีกครั้ง</Button>
          </div>
          <Button variant="ghost" className="self-center" onClick={close}>ไว้ทีหลัง</Button>
        </div>
      ) : (
        <div role="status" className="flex flex-col items-center gap-4 py-4 text-center">
          <span className="grid size-14 place-items-center rounded-full bg-ok-soft"><Check className="size-7 text-ok" strokeWidth={2.6} aria-hidden /></span>
          <h2 className="text-xl font-semibold">เรียบร้อย</h2>
          <p className="text-[15px] text-muted">{name} ไลฟ์ต่อได้ตามปกติ</p>
          <Button className="h-12 px-7" onClick={close}>ปิด</Button>
        </div>
      )}
    </Dialog>
  )
}
