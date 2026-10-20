import { memo } from 'react'
import { Globe, Plus } from 'lucide-react'
import type { ProxyItem } from '@/lib/types'
import { cn } from '@/lib/cn'
import { Badge, Button, EmptyState, Skeleton, StatusDot, Switch, type Tone } from '@/components/ui/base'
import { TestPill } from './proxyUi'

const dotTone = (t: ProxyItem['test']): Tone => (t === 'testing' ? 'warn' : !t ? 'neutral' : t.ok ? 'ok' : 'bad')

export const ProxyRow = memo(function ProxyRow({ p, active, used, onSelect, onToggle }: {
  p: ProxyItem; active: boolean; used: number; onSelect: (id: string) => void; onToggle: (id: string, v: boolean) => void
}) {
  return (
    <div className={cn('cv-auto group relative flex items-center gap-2 rounded-ctl pr-3 transition-colors', active ? 'bg-surface-2' : 'hover:bg-surface-2/60', !p.enabled && 'opacity-75')}>
      <span className={cn('absolute left-0 h-5 w-0.5 rounded-full bg-primary transition-opacity', active ? 'opacity-100' : 'opacity-0')} />
      <button type="button" onClick={() => onSelect(p.id)} aria-current={active ? 'true' : undefined} className="flex min-w-0 flex-1 items-center gap-3 rounded-ctl py-2.5 pl-3 text-left">
        <StatusDot tone={dotTone(p.test)} pulse={p.test === 'testing'} />
        <span className="min-w-0 flex-1">
          <span className="flex items-center gap-2">
            <span className="truncate text-sm font-medium">{p.name || 'ไม่มีชื่อ'}</span>
            <Badge className="h-5 px-1.5 text-[11px]">{p.protocol}</Badge>
          </span>
          <span className="mt-0.5 flex items-center justify-between gap-2">
            <span className={cn('truncate font-mono text-xs', p.host ? 'text-subtle' : 'text-warn')}>{p.host ? `${p.host}:${p.port}` : 'ยังไม่ตั้ง host'}</span>
            <TestPill test={p.test} />
          </span>
        </span>
      </button>
      <span className="flex shrink-0 flex-col items-end gap-0.5">
        <Switch size="sm" checked={p.enabled} onChange={(v) => onToggle(p.id, v)} label={`เปิดใช้ ${p.name}`} />
        <span className="text-[11px] text-subtle tnum">{used} บัญชี</span>
      </span>
    </div>
  )
})

export function ProxyListSkeleton() {
  return (
    <div className="flex flex-col gap-0.5 px-2 pb-2" aria-busy="true" aria-label="กำลังโหลด">
      {Array.from({ length: 4 }, (_, i) => <Skeleton key={i} className="h-[58px] rounded-ctl" />)}
    </div>
  )
}

export function ProxyEmpty({ onAdd }: { onAdd: () => void }) {
  return (
    <div className="grid h-full place-items-center rounded-card bg-surface">
      <EmptyState
        icon={<Globe />} title="ยังไม่มี Proxy"
        hint="เพิ่ม Proxy แรก ผูกกับบัญชี แล้วกดบันทึกและเปิดใช้ Native Proxy"
        action={<Button icon={<Plus className="size-4" />} onClick={onAdd}>เพิ่ม Proxy แรก</Button>}
      />
    </div>
  )
}
