import { forwardRef, useId, useState, type InputHTMLAttributes, type ReactNode, type SelectHTMLAttributes, type TextareaHTMLAttributes } from 'react'
import { ChevronDown, CircleHelp, Eye, EyeOff } from 'lucide-react'
import { cn } from '@/lib/cn'

const control =
  'w-full rounded-ctl bg-surface-2 px-3 text-sm text-fg placeholder:text-subtle transition-shadow ' +
  'hover:bg-surface-3/70 focus:bg-surface focus:outline-none focus:shadow-[0_0_0_2px_var(--primary)] ' +
  'disabled:cursor-not-allowed disabled:opacity-50 aria-[invalid=true]:shadow-[0_0_0_2px_var(--bad)]'

/** ป้ายกำกับ + ช่องกรอก + คำใบ้ (คำใบ้ซ่อนใน tooltip ไม่กางข้อความ) */
export function Field({ label, hint, error, children, className, inline }: {
  label: string; hint?: string; error?: string; children: ReactNode; className?: string; inline?: boolean
}) {
  return (
    <label className={cn(inline ? 'flex items-center justify-between gap-4' : 'block', className)}>
      <span className={cn('flex items-center gap-1.5 text-[13px] font-medium text-muted', !inline && 'mb-1.5')}>
        {label}
        {hint && (
          <span className="group/h relative inline-flex">
            <CircleHelp className="size-3.5 text-subtle" aria-hidden />
            <span role="tooltip" className="pointer-events-none absolute bottom-full left-1/2 z-50 mb-1.5 w-56 -translate-x-1/2 rounded-md bg-fg px-2.5 py-1.5 text-xs font-normal text-bg opacity-0 shadow-float transition-opacity group-hover/h:opacity-100">
              {hint}
            </span>
          </span>
        )}
      </span>
      {children}
      {error && <span className="mt-1 block text-xs text-bad">{error}</span>}
    </label>
  )
}

export const Input = forwardRef<HTMLInputElement, InputHTMLAttributes<HTMLInputElement> & { mono?: boolean }>(function Input(
  { className, mono, ...rest }, ref,
) {
  return <input ref={ref} className={cn(control, 'h-9', mono && 'font-mono text-[13px]', className)} {...rest} />
})

export function NumberInput({ value, onChange, min, max, step = 1, suffix, className, ...rest }: {
  value: number; onChange: (n: number) => void; min?: number; max?: number; step?: number; suffix?: string; className?: string
} & Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'onChange' | 'min' | 'max' | 'step'>) {
  return (
    <div className={cn('relative', className)}>
      <input
        type="number" inputMode="numeric" value={Number.isFinite(value) ? value : ''} min={min} max={max} step={step}
        onChange={(e) => onChange(e.target.value === '' ? NaN : Number(e.target.value))}
        onBlur={(e) => {
          let n = Number(e.target.value)
          if (!Number.isFinite(n)) n = min ?? 0
          if (min != null) n = Math.max(min, n)
          if (max != null) n = Math.min(max, n)
          if (n !== value) onChange(n)
        }}
        className={cn(control, 'h-9 tnum', suffix && 'pr-10')} {...rest}
      />
      {suffix && <span className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-xs text-subtle">{suffix}</span>}
    </div>
  )
}

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaHTMLAttributes<HTMLTextAreaElement> & { mono?: boolean }>(function Textarea(
  { className, mono, ...rest }, ref,
) {
  return <textarea ref={ref} className={cn(control, 'min-h-20 resize-y py-2 leading-relaxed', mono && 'font-mono text-[13px]', className)} {...rest} />
})

export function Select({ className, children, ...rest }: SelectHTMLAttributes<HTMLSelectElement>) {
  return (
    <div className="relative">
      <select className={cn(control, 'h-9 appearance-none pr-9', className)} {...rest}>{children}</select>
      <ChevronDown className="pointer-events-none absolute right-3 top-1/2 size-4 -translate-y-1/2 text-subtle" aria-hidden />
    </div>
  )
}

export function Slider({ value, onChange, min = 0, max = 100, label }: {
  value: number; onChange: (n: number) => void; min?: number; max?: number; label: string
}) {
  const pct = ((value - min) / (max - min)) * 100
  return (
    <div className="flex items-center gap-3">
      <input
        type="range" aria-label={label} min={min} max={max} value={value} onChange={(e) => onChange(Number(e.target.value))}
        className="h-1.5 w-full cursor-pointer appearance-none rounded-full accent-[var(--primary)]"
        style={{ background: `linear-gradient(to right, var(--primary) ${pct}%, var(--surface-3) ${pct}%)` }}
      />
      <span className="w-9 shrink-0 text-right text-sm tnum text-muted">{value}</span>
    </div>
  )
}

export function ColorInput({ value, onChange, label }: { value: string; onChange: (v: string) => void; label: string }) {
  const id = useId()
  return (
    <div className="flex h-9 items-center gap-2 rounded-ctl bg-surface-2 pl-1.5 pr-3">
      <input id={id} aria-label={label} type="color" value={value} onChange={(e) => onChange(e.target.value)} className="size-6 cursor-pointer rounded-md border-0 bg-transparent p-0" />
      <span className="font-mono text-[13px] uppercase text-muted">{value}</span>
    </div>
  )
}

/** แถวสวิตช์พร้อมคำอธิบายสั้น 1 บรรทัด */
export function ToggleRow({ title, sub, children }: { title: string; sub?: string; children: ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-4 py-2.5">
      <div className="min-w-0">
        <p className="text-sm font-medium text-fg">{title}</p>
        {sub && <p className="truncate text-xs text-subtle">{sub}</p>}
      </div>
      {children}
    </div>
  )
}


/** ช่องรหัสผ่าน/โทเคน พร้อมปุ่มสลับแสดง-ซ่อน (ซ่อนเป็นค่าเริ่มต้น) */
export const SecretInput = forwardRef<HTMLInputElement, Omit<InputHTMLAttributes<HTMLInputElement>, 'type'> & { mono?: boolean }>(function SecretInput(
  { className, mono, ...rest }, ref,
) {
  const [show, setShow] = useState(false)
  return (
    <div className="relative">
      <input
        ref={ref} type={show ? 'text' : 'password'} autoComplete="off" spellCheck={false}
        className={cn(control, 'h-9 pr-10', mono && 'font-mono text-[13px]', className)} {...rest}
      />
      <button
        type="button" aria-label={show ? 'ซ่อนค่า' : 'แสดงค่า'} aria-pressed={show} onClick={() => setShow((v) => !v)}
        className="absolute right-1 top-1/2 grid size-7 -translate-y-1/2 place-items-center rounded-md text-subtle transition-colors hover:text-fg"
      >
        {show ? <EyeOff className="size-4" aria-hidden /> : <Eye className="size-4" aria-hidden />}
      </button>
    </div>
  )
})
