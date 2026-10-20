import { CopyButton } from '@/components/ui/base'
import { cn } from '@/lib/cn'

/** ค่าอ่านอย่างเดียว พร้อมปุ่มคัดลอก */
export function ValueRow({ label, value, mono, copy = true }: { label: string; value?: string; mono?: boolean; copy?: boolean }) {
  const has = !!value
  return (
    <div className="min-w-0">
      <p className="mb-1.5 text-[13px] font-medium text-muted">{label}</p>
      <div className="flex h-9 items-center gap-1 rounded-ctl bg-surface-2 pl-3 pr-1">
        <span className={cn('min-w-0 flex-1 truncate text-sm', mono && 'font-mono text-[13px]', has ? 'text-fg' : 'text-subtle')} title={value}>{has ? value : '—'}</span>
        {copy && has && <CopyButton text={value!} label={`คัดลอก${label}`} />}
      </div>
    </div>
  )
}
