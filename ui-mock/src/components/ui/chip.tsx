import type { ReactNode } from 'react'
import { cn } from '@/lib/cn'
import { toneClass, type Tone } from './base'

/** ชิปกดได้: ใช้เป็นตัวกรองแบบสลับ (ส่ง `pressed`) หรือคำสั่งด่วน (ไม่ส่ง `pressed`) */
export function Chip({ pressed, onClick, children, count, tone, dot, icon, disabled, title, className }: {
  pressed?: boolean
  onClick: () => void
  children: ReactNode
  count?: number
  tone?: Tone
  /** จุดสีสถานะหน้าป้าย (สถานะไม่ได้บอกด้วยสีอย่างเดียว เพราะมีข้อความและ aria-pressed กำกับ) */
  dot?: boolean
  icon?: ReactNode
  disabled?: boolean
  title?: string
  className?: string
}) {
  const toggle = pressed !== undefined
  return (
    <button
      type="button" onClick={onClick} disabled={disabled} title={title}
      aria-pressed={toggle ? pressed : undefined}
      className={cn(
        'inline-flex h-7 shrink-0 items-center gap-1.5 whitespace-nowrap rounded-full px-2.5 text-xs font-medium transition-colors duration-150',
        'disabled:cursor-not-allowed disabled:opacity-45',
        toggle && pressed ? (tone ? toneClass.soft[tone] : 'bg-primary-soft text-primary') : 'bg-surface-2 text-muted hover:bg-surface-3 hover:text-fg',
        toggle && !pressed && 'text-subtle',
        className,
      )}
    >
      {dot && tone && <span aria-hidden className={cn('size-1.5 rounded-full', toneClass.bg[tone], toggle && !pressed && 'opacity-40')} />}
      {icon && <span aria-hidden className="[&>svg]:size-3.5">{icon}</span>}
      {children}
      {count != null && <span className="tnum opacity-80">{count}</span>}
    </button>
  )
}
