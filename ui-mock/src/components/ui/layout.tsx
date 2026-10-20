import { useRef, type ReactNode } from 'react'
import { cn } from '@/lib/cn'

/* ───────── Tabs (ARIA + ลูกศรซ้ายขวา) ───────── */
export interface TabDef<T extends string> { id: T; label: string; icon?: ReactNode; badge?: ReactNode }

export function Tabs<T extends string>({ tabs, value, onChange, label, className, trailing }: {
  tabs: TabDef<T>[]; value: T; onChange: (id: T) => void; label: string; className?: string; trailing?: ReactNode
}) {
  const ref = useRef<HTMLDivElement>(null)
  const onKey = (e: React.KeyboardEvent) => {
    const i = tabs.findIndex((t) => t.id === value)
    const next = e.key === 'ArrowRight' ? i + 1 : e.key === 'ArrowLeft' ? i - 1 : null
    if (next == null) return
    const t = tabs[(next + tabs.length) % tabs.length]
    onChange(t.id)
    ref.current?.querySelector<HTMLElement>(`[data-tab="${t.id}"]`)?.focus()
    e.preventDefault()
  }
  return (
    <div className={cn('flex items-end justify-between gap-3', className)}>
      <div ref={ref} role="tablist" aria-label={label} onKeyDown={onKey} className="flex min-w-0 gap-1 overflow-x-auto overflow-y-hidden">
        {tabs.map((t) => {
          const active = t.id === value
          return (
            <button
              key={t.id} data-tab={t.id} role="tab" type="button" aria-selected={active} tabIndex={active ? 0 : -1}
              onClick={() => onChange(t.id)}
              className={cn(
                'relative inline-flex h-10 shrink-0 items-center gap-2 px-3 text-sm font-medium whitespace-nowrap transition-colors',
                active ? 'text-fg' : 'text-subtle hover:text-fg',
              )}
            >
              {t.icon && <span className="[&>svg]:size-4">{t.icon}</span>}
              {t.label}
              {t.badge}
              <span className={cn('absolute inset-x-2 bottom-0 h-0.5 origin-center rounded-full bg-primary transition-transform duration-200 ease-out-expo', active ? 'scale-x-100' : 'scale-x-0')} />
            </button>
          )
        })}
      </div>
      {trailing}
    </div>
  )
}

/* ───────── Layout helpers ───────── */
export function PageHeader({ title, sub, actions }: { title: string; sub?: ReactNode; actions?: ReactNode }) {
  return (
    <div className="flex flex-wrap items-center justify-between gap-3 px-6 pb-4 pt-5">
      <div className="min-w-0">
        <h1 className="text-xl font-semibold tracking-tight">{title}</h1>
        {sub && <div className="mt-0.5 text-sm text-subtle">{sub}</div>}
      </div>
      {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
    </div>
  )
}

/** กลุ่มเนื้อหาแบบไม่มีกรอบ — แยกด้วยช่องว่างและหัวข้อ */
export function Section({ title, aside, children, className }: { title?: string; aside?: ReactNode; children: ReactNode; className?: string }) {
  return (
    <section className={cn('py-5', className)}>
      {(title || aside) && (
        <div className="mb-3 flex items-center justify-between gap-3">
          {title && <h3 className="text-[13px] font-semibold uppercase tracking-wide text-subtle">{title}</h3>}
          {aside}
        </div>
      )}
      {children}
    </section>
  )
}

export function Surface({ children, className }: { children: ReactNode; className?: string }) {
  return <div className={cn('rounded-card bg-surface', className)}>{children}</div>
}

/** ตารางข้อมูลแบบไม่มีเส้นกั้นแถว ใช้ hover แทน */
export function DataTable({ head, children, className }: { head: { label: string; className?: string }[]; children: ReactNode; className?: string }) {
  return (
    <table className={cn('w-full border-separate border-spacing-0 text-sm', className)}>
      <thead>
        <tr>
          {head.map((h) => (
            <th key={h.label} className={cn('sticky top-0 z-10 bg-surface px-3 py-2 text-left text-xs font-medium text-subtle first:pl-4 last:pr-4', h.className)}>{h.label}</th>
          ))}
        </tr>
      </thead>
      <tbody>{children}</tbody>
    </table>
  )
}

export const tdc = 'px-3 py-2.5 first:pl-4 last:pr-4 align-middle'

