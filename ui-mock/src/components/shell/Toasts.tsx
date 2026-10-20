import { CircleAlert, CircleCheck, Info, TriangleAlert, X } from 'lucide-react'
import { useApp } from '@/lib/store'
import { cn } from '@/lib/cn'
import { toneClass } from '@/components/ui/base'

const icons = { info: Info, ok: CircleCheck, warn: TriangleAlert, bad: CircleAlert } as const

export function Toasts() {
  const toasts = useApp((s) => s.toasts)
  const dismiss = useApp((s) => s.dismissToast)
  return (
    <div role="status" aria-live="polite" className="pointer-events-none fixed bottom-10 right-5 z-[80] flex w-80 flex-col gap-2">
      {toasts.map((t) => {
        const Icon = icons[t.tone]
        return (
          <div key={t.id} className="pointer-events-auto flex animate-rise items-start gap-3 rounded-card bg-surface p-3 shadow-float hairline">
            <span className={cn('mt-0.5 grid size-6 shrink-0 place-items-center rounded-full', toneClass.soft[t.tone])}><Icon className="size-3.5" aria-hidden /></span>
            <div className="min-w-0 flex-1">
              <p className="truncate text-sm font-medium">{t.title}</p>
              {t.detail && <p className="truncate text-xs text-subtle">{t.detail}</p>}
            </div>
            <button type="button" aria-label="ปิด" onClick={() => dismiss(t.id)} className="rounded-md p-1 text-subtle hover:bg-surface-2 hover:text-fg"><X className="size-3.5" /></button>
          </div>
        )
      })}
    </div>
  )
}
