import { useNavigate } from 'react-router-dom'
import { BellOff, CircleAlert, CircleCheck, Info, TriangleAlert } from 'lucide-react'
import { useApp } from '@/lib/store'
import { ago, useNow } from '@/lib/format'
import { cn } from '@/lib/cn'
import { Button, EmptyState, toneClass, type Tone } from '@/components/ui/base'
import { Sheet } from '@/components/ui/overlay'

const icon = { info: Info, ok: CircleCheck, warn: TriangleAlert, bad: CircleAlert } as const
const toneOf = (t: 'info' | 'ok' | 'warn' | 'bad'): Tone => (t === 'info' ? 'info' : t)

export function NotificationPanel({ open, onClose }: { open: boolean; onClose: () => void }) {
  const nav = useNavigate()
  const items = useApp((s) => s.notifications)
  const markAll = useApp((s) => s.markAllRead)
  const clear = useApp((s) => s.clearNotifications)
  const now = useNow(5000)

  return (
    <Sheet
      open={open} onClose={onClose} title="การแจ้งเตือน" width={400}
      footer={
        <div className="flex justify-between">
          <Button variant="ghost" size="sm" onClick={markAll}>อ่านทั้งหมด</Button>
          <Button variant="ghost" size="sm" onClick={clear}>ล้างรายการ</Button>
        </div>
      }
    >
      {items.length === 0 ? (
        <EmptyState icon={<BellOff />} title="ไม่มีการแจ้งเตือน" hint="Captcha ที่พบและงานที่ล้มเหลวจะขึ้นที่นี่" />
      ) : (
        <ul className="flex flex-col gap-0.5">
          {items.map((n) => {
            const Icon = icon[n.tone]
            return (
              <li key={n.id}>
                <button
                  type="button"
                  onClick={() => { onClose(); if (n.instanceId) nav(`/accounts/${n.instanceId}${n.title.includes('Captcha') ? '/captcha' : ''}`) }}
                  className="flex w-full items-start gap-3 rounded-ctl p-2.5 text-left transition-colors hover:bg-surface-2"
                >
                  <span className={cn('mt-0.5 grid size-7 shrink-0 place-items-center rounded-full', toneClass.soft[toneOf(n.tone)])}><Icon className="size-4" aria-hidden /></span>
                  <span className="min-w-0 flex-1">
                    <span className="flex items-baseline justify-between gap-2">
                      <span className={cn('truncate text-sm', n.read ? 'text-muted' : 'font-medium text-fg')}>{n.title}</span>
                      <span className="shrink-0 text-xs text-subtle">{ago(n.at, now)}</span>
                    </span>
                    {n.detail && <span className="block truncate text-xs text-subtle">{n.detail}</span>}
                  </span>
                  {!n.read && <span className="mt-2 size-1.5 shrink-0 rounded-full bg-primary" aria-label="ยังไม่อ่าน" />}
                </button>
              </li>
            )
          })}
        </ul>
      )}
    </Sheet>
  )
}
