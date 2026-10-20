import { forwardRef, useState, type ButtonHTMLAttributes, type ReactNode } from 'react'
import { Check, Copy, LoaderCircle } from 'lucide-react'
import { cn } from '@/lib/cn'

/* ───────── Button ───────── */
type Variant = 'primary' | 'secondary' | 'ghost' | 'danger'
type Size = 'sm' | 'md'

const variants: Record<Variant, string> = {
  primary: 'bg-primary text-on-primary hover:bg-primary-strong',
  secondary: 'bg-surface-2 text-fg hover:bg-surface-3',
  ghost: 'text-muted hover:bg-surface-2 hover:text-fg',
  danger: 'bg-bad-soft text-bad hover:bg-bad hover:text-on-primary',
}
const sizes: Record<Size, string> = { sm: 'h-8 px-2.5 text-[13px] gap-1.5', md: 'h-9 px-3.5 text-sm gap-2' }

interface BtnProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: Variant
  size?: Size
  loading?: boolean
  icon?: ReactNode
}

export const Button = forwardRef<HTMLButtonElement, BtnProps>(function Button(
  { variant = 'secondary', size = 'md', loading, icon, className, children, disabled, ...rest }, ref,
) {
  return (
    <button
      ref={ref}
      type="button"
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      className={cn(
        'relative inline-flex shrink-0 items-center justify-center whitespace-nowrap rounded-ctl font-medium transition-colors duration-150',
        'disabled:cursor-not-allowed disabled:opacity-45 active:translate-y-px',
        variants[variant], sizes[size], className,
      )}
      {...rest}
    >
      {loading ? <LoaderCircle className="size-4 animate-spin" aria-hidden /> : icon}
      {children}
    </button>
  )
})

export const IconButton = forwardRef<HTMLButtonElement, BtnProps & { label: string; tip?: boolean }>(function IconButton(
  { label, variant = 'ghost', size = 'md', className, children, tip = true, ...rest }, ref,
) {
  return (
    <span className="group/tip relative inline-flex">
      <button
        ref={ref}
        type="button"
        aria-label={label}
        className={cn(
          'inline-flex shrink-0 items-center justify-center rounded-ctl transition-colors duration-150 disabled:cursor-not-allowed disabled:opacity-45',
          size === 'sm' ? 'size-8' : 'size-9', variants[variant], className,
        )}
        {...rest}
      >
        {children}
      </button>
      {tip && (
        <span role="tooltip" className="pointer-events-none absolute left-1/2 top-full z-50 mt-1.5 -translate-x-1/2 whitespace-nowrap rounded-md bg-fg px-2 py-1 text-xs text-bg opacity-0 shadow-float transition-opacity delay-300 group-hover/tip:opacity-100 group-focus-within/tip:opacity-100">
          {label}
        </span>
      )}
    </span>
  )
})

/* ───────── Status ───────── */
export type Tone = 'ok' | 'warn' | 'bad' | 'info' | 'neutral'
const toneText: Record<Tone, string> = { ok: 'text-ok', warn: 'text-warn', bad: 'text-bad', info: 'text-primary', neutral: 'text-subtle' }
const toneBg: Record<Tone, string> = { ok: 'bg-ok', warn: 'bg-warn', bad: 'bg-bad', info: 'bg-primary', neutral: 'bg-subtle' }
const toneSoft: Record<Tone, string> = { ok: 'bg-ok-soft text-ok', warn: 'bg-warn-soft text-warn', bad: 'bg-bad-soft text-bad', info: 'bg-primary-soft text-primary', neutral: 'bg-surface-2 text-muted' }

export const toneClass = { text: toneText, bg: toneBg, soft: toneSoft }

export function StatusDot({ tone = 'neutral', pulse, className }: { tone?: Tone; pulse?: boolean; className?: string }) {
  return (
    <span className={cn('relative inline-flex size-2 shrink-0', className)} aria-hidden>
      {pulse && <span className={cn('absolute inset-0 rounded-full animate-ring', toneBg[tone])} />}
      <span className={cn('relative size-2 rounded-full', toneBg[tone])} />
    </span>
  )
}

export function Badge({ tone = 'neutral', children, dot, className }: { tone?: Tone; children: ReactNode; dot?: boolean; className?: string }) {
  return (
    <span className={cn('inline-flex h-[22px] items-center gap-1.5 rounded-full px-2 text-xs font-medium whitespace-nowrap', toneSoft[tone], className)}>
      {dot && <span className={cn('size-1.5 rounded-full', toneBg[tone])} />}
      {children}
    </span>
  )
}

/* ───────── Switch ───────── */
export function Switch({ checked, onChange, label, disabled, size = 'md' }: {
  checked: boolean; onChange: (v: boolean) => void; label: string; disabled?: boolean; size?: 'sm' | 'md'
}) {
  return (
    <button
      type="button" role="switch" aria-checked={checked} aria-label={label} disabled={disabled}
      onClick={() => onChange(!checked)}
      className={cn(
        'relative inline-flex shrink-0 items-center rounded-full transition-colors duration-200 disabled:opacity-45 disabled:cursor-not-allowed',
        size === 'sm' ? 'h-4.5 w-8' : 'h-5.5 w-10', checked ? 'bg-primary' : 'bg-surface-3',
      )}
    >
      <span className={cn(
        'absolute left-0.5 rounded-full bg-white shadow-sm transition-transform duration-200 ease-out-quart',
        size === 'sm' ? 'size-3.5' : 'size-4.5', checked ? (size === 'sm' ? 'translate-x-3.5' : 'translate-x-4.5') : 'translate-x-0',
      )} />
    </button>
  )
}

/* ───────── Segmented ───────── */
export function Segmented<T extends string | number>({ value, onChange, options, label, size = 'md' }: {
  value: T; onChange: (v: T) => void; options: { value: T; label: ReactNode }[]; label: string; size?: 'sm' | 'md'
}) {
  return (
    <div role="radiogroup" aria-label={label} className="inline-flex rounded-ctl bg-surface-2 p-0.5">
      {options.map((o) => (
        <button
          key={String(o.value)} type="button" role="radio" aria-checked={o.value === value}
          onClick={() => onChange(o.value)}
          className={cn(
            'rounded-[8px] px-3 font-medium whitespace-nowrap transition-colors',
            size === 'sm' ? 'h-7 text-xs' : 'h-8 text-[13px]',
            o.value === value ? 'bg-surface text-fg shadow-sm' : 'text-muted hover:text-fg',
          )}
        >
          {o.label}
        </button>
      ))}
    </div>
  )
}

/* ───────── Loading ───────── */
export function Skeleton({ className }: { className?: string }) {
  return <div aria-hidden className={cn('animate-pulse-soft rounded-md bg-surface-3/70', className)} />
}

export function Spinner({ className }: { className?: string }) {
  return <LoaderCircle aria-hidden className={cn('size-4 animate-spin', className)} />
}

/* ───────── Avatar ───────── */
export function Avatar({ name, hue, size = 40, className }: { name: string; hue: number; size?: number; className?: string }) {
  const initial = (name.trim()[0] || '?').toUpperCase()
  return (
    <span
      aria-hidden
      className={cn('inline-grid shrink-0 place-items-center rounded-full font-semibold text-white select-none', className)}
      style={{
        width: size, height: size, fontSize: size * 0.4,
        background: `linear-gradient(145deg, oklch(0.66 0.16 ${hue}), oklch(0.5 0.17 ${(hue + 40) % 360}))`,
      }}
    >
      {initial}
    </span>
  )
}

/* ───────── Meter ───────── */
export function Meter({ value, max, tone = 'info', className }: { value: number; max: number; tone?: Tone; className?: string }) {
  const pct = Math.min(100, Math.round((value / Math.max(1, max)) * 100))
  return (
    <div role="meter" aria-valuenow={value} aria-valuemin={0} aria-valuemax={max} className={cn('h-1.5 overflow-hidden rounded-full bg-surface-3', className)}>
      <div className={cn('h-full origin-left rounded-full transition-transform duration-500 ease-out-expo', toneBg[tone])} style={{ transform: `scaleX(${pct / 100})` }} />
    </div>
  )
}

/* ───────── Misc ───────── */
export function Kbd({ children }: { children: ReactNode }) {
  return <kbd className="rounded-md bg-surface-2 px-1.5 py-0.5 font-mono text-[11px] text-muted">{children}</kbd>
}

export function CopyButton({ text, label = 'คัดลอก', size = 'sm' }: { text: string; label?: string; size?: 'sm' | 'md' }) {
  const [done, setDone] = useState(false)
  return (
    <IconButton
      label={done ? 'คัดลอกแล้ว' : label} size={size}
      onClick={() => { void navigator.clipboard?.writeText(text); setDone(true); window.setTimeout(() => setDone(false), 1400) }}
    >
      {done ? <Check className="size-4 text-ok" /> : <Copy className="size-4" />}
    </IconButton>
  )
}

export function EmptyState({ icon, title, hint, action }: { icon: ReactNode; title: string; hint?: string; action?: ReactNode }) {
  return (
    <div className="grid place-items-center px-6 py-14 text-center animate-fade-in">
      <div className="mb-3 grid size-12 place-items-center rounded-2xl bg-surface-2 text-subtle [&>svg]:size-6">{icon}</div>
      <p className="font-medium text-fg">{title}</p>
      {hint && <p className="mt-1 max-w-xs text-sm text-subtle">{hint}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  )
}

export function Stat({ label, value, sub, tone }: { label: string; value: ReactNode; sub?: ReactNode; tone?: Tone }) {
  return (
    <div className="min-w-0">
      <p className="truncate text-xs text-subtle">{label}</p>
      <p className={cn('mt-0.5 text-xl font-semibold leading-tight tracking-tight', tone && toneText[tone])}>{value}</p>
      {sub && <p className="mt-0.5 truncate text-xs text-subtle">{sub}</p>}
    </div>
  )
}
