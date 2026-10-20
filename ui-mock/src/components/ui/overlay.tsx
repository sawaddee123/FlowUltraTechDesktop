import { useEffect, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { X } from 'lucide-react'
import { cn } from '@/lib/cn'
import { IconButton } from './base'

/* ───────── Popover menu ───────── */
export interface MenuItem {
  label: string
  icon?: ReactNode
  onSelect: () => void
  danger?: boolean
  disabled?: boolean
  divider?: boolean
  hint?: string
}

export function Menu({ trigger, items, align = 'right', width = 224 }: {
  trigger: (p: { open: boolean; toggle: () => void }) => ReactNode
  items: MenuItem[]
  align?: 'left' | 'right'
  width?: number
}) {
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!open) return
    const onDoc = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false) }
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false) }
    document.addEventListener('mousedown', onDoc)
    document.addEventListener('keydown', onKey)
    return () => { document.removeEventListener('mousedown', onDoc); document.removeEventListener('keydown', onKey) }
  }, [open])

  return (
    <div ref={ref} className="relative inline-flex">
      {trigger({ open, toggle: () => setOpen((o) => !o) })}
      {open && (
        <div
          role="menu" style={{ width }}
          className={cn('absolute top-full z-50 mt-1.5 animate-rise rounded-card bg-surface p-1.5 shadow-float hairline', align === 'right' ? 'right-0' : 'left-0')}
        >
          {items.map((it, i) => (
            <div key={i}>
              {it.divider && <div className="my-1 h-px bg-line" />}
              <button
                type="button" role="menuitem" disabled={it.disabled}
                onClick={() => { setOpen(false); it.onSelect() }}
                className={cn(
                  'flex w-full items-center gap-2.5 rounded-lg px-2.5 py-2 text-left text-sm transition-colors disabled:opacity-45',
                  it.danger ? 'text-bad hover:bg-bad-soft' : 'text-fg hover:bg-surface-2',
                )}
              >
                {it.icon && <span className="text-subtle [&>svg]:size-4">{it.icon}</span>}
                <span className="flex-1">{it.label}</span>
                {it.hint && <span className="text-xs text-subtle">{it.hint}</span>}
              </button>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}

/* ───────── Sheet (ลิ้นชักด้านข้าง — แทน modal เพื่อไม่บังหน้าหลัก) ───────── */
export function Sheet({ open, onClose, title, children, footer, width = 420, dismissible = true, scrim = true }: {
  open: boolean; onClose: () => void; title: string; children: ReactNode; footer?: ReactNode
  width?: number; dismissible?: boolean; scrim?: boolean
}) {
  useEffect(() => {
    if (!open || !dismissible) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [open, dismissible, onClose])
  if (!open) return null
  return createPortal(
    <div className="fixed inset-0 z-[60]">
      {scrim && <div className="absolute inset-0 animate-fade-in" style={{ background: 'var(--scrim)' }} onClick={dismissible ? onClose : undefined} />}
      <aside
        role="dialog" aria-modal="true" aria-label={title} style={{ width }}
        className="absolute inset-y-0 right-0 flex max-w-full animate-slide-in flex-col bg-surface shadow-float hairline"
      >
        <header className="flex h-14 shrink-0 items-center justify-between px-5">
          <h2 className="text-base font-semibold">{title}</h2>
          {dismissible && <IconButton label="ปิด" onClick={onClose}><X className="size-4" /></IconButton>}
        </header>
        <div className="scroll-y min-h-0 flex-1 px-5 pb-5">{children}</div>
        {footer && <footer className="shrink-0 bg-surface-2/60 px-5 py-3">{footer}</footer>}
      </aside>
    </div>,
    document.body,
  )
}

/* ───────── Confirm (ใช้เฉพาะการลบถาวร) ───────── */
export function Confirm({ open, title, detail, confirmLabel, onConfirm, onCancel }: {
  open: boolean; title: string; detail?: string; confirmLabel: string; onConfirm: () => void; onCancel: () => void
}) {
  useEffect(() => {
    if (!open) return
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onCancel() }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [open, onCancel])
  if (!open) return null
  return createPortal(
    <div className="fixed inset-0 z-[70] grid place-items-center p-6">
      <div className="absolute inset-0 animate-fade-in" style={{ background: 'var(--scrim)' }} onClick={onCancel} />
      <div role="alertdialog" aria-modal="true" aria-label={title} className="relative w-full max-w-sm animate-rise rounded-card bg-surface p-5 shadow-float hairline">
        <h2 className="text-base font-semibold">{title}</h2>
        {detail && <p className="mt-1 text-sm text-muted">{detail}</p>}
        <div className="mt-5 flex justify-end gap-2">
          <button type="button" onClick={onCancel} className="h-9 rounded-ctl px-3.5 text-sm font-medium text-muted hover:bg-surface-2">ยกเลิก</button>
          <button type="button" onClick={onConfirm} className="h-9 rounded-ctl bg-bad px-3.5 text-sm font-medium text-on-primary hover:opacity-90">{confirmLabel}</button>
        </div>
      </div>
    </div>,
    document.body,
  )
}
