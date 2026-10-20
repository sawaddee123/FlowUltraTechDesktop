import { LogIn, Play, TriangleAlert } from 'lucide-react'
import type { Instance } from '@/lib/types'
import { useApp, selectRunning } from '@/lib/store'
import { Button, Spinner } from '@/components/ui/base'

/** แถบเตือนสั้นๆ เมื่อฟีเจอร์ต้องการให้บัญชีทำงานอยู่ พร้อมปุ่มลัดไปแก้ที่ต้นเหตุ */
export function NeedRunning({ instance: i, text }: { instance: Instance; text: string }) {
  const start = useApp((s) => s.startInstance)
  const openLogin = useApp((s) => s.openLogin)
  const quotaFull = useApp((s) => selectRunning(s) >= s.license.maxAccounts)
  if (i.status === 'running') return null
  const starting = i.status === 'starting'
  return (
    <div role="status" className="flex items-center gap-3 rounded-ctl bg-warn-soft px-3.5 py-2 text-sm text-warn">
      {starting ? <Spinner className="shrink-0" /> : <TriangleAlert className="size-4 shrink-0" aria-hidden />}
      <span className="min-w-0 flex-1">{starting ? 'กำลังเริ่มบัญชี…' : text}</span>
      {!starting && (i.loggedIn ? (
        <Button size="sm" disabled={quotaFull} title={quotaFull ? 'เปิดครบโควต้าสูงสุดแล้ว' : undefined}
          icon={<Play className="size-3.5 fill-current" />} onClick={() => start(i.id)}>เริ่มบัญชี</Button>
      ) : (
        <Button size="sm" icon={<LogIn className="size-3.5" />} onClick={() => openLogin(i.id)}>เข้าสู่ระบบ</Button>
      ))}
    </div>
  )
}
