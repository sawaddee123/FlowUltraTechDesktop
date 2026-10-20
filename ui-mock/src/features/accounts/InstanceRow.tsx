import { memo } from 'react'
import { Globe, Eye, EyeOff, ScanSearch } from 'lucide-react'
import type { Instance } from '@/lib/types'
import { displayName, fmtNum, statusMeta } from '@/lib/format'
import { cn } from '@/lib/cn'
import { Avatar, StatusDot } from '@/components/ui/base'

export const InstanceRow = memo(function InstanceRow({ inst, active, onSelect }: { inst: Instance; active: boolean; onSelect: (id: string) => void }) {
  const meta = statusMeta[inst.status]
  const sub = inst.loggedIn ? '@' + inst.handle : 'ยังไม่เข้าสู่ระบบ'
  return (
    <button
      type="button" onClick={() => onSelect(inst.id)} aria-current={active ? 'true' : undefined}
      className={cn(
        'cv-auto group relative flex w-full items-center gap-3 rounded-ctl px-3 py-2.5 text-left transition-colors',
        active ? 'bg-surface-2' : 'hover:bg-surface-2/60',
      )}
    >
      <span className={cn('absolute left-0 h-5 w-0.5 rounded-full bg-primary transition-opacity', active ? 'opacity-100' : 'opacity-0')} />
      <span className="relative shrink-0">
        <Avatar name={inst.loggedIn ? displayName(inst) : '?'} hue={inst.hue} size={38} className={cn(!inst.loggedIn && 'opacity-50')} />
        <span className="absolute -bottom-0.5 -right-0.5 rounded-full bg-surface p-[3px] group-hover:bg-surface-2"><StatusDot tone={meta.tone} pulse={inst.status === 'starting'} /></span>
      </span>
      <span className="min-w-0 flex-1">
        <span className="flex items-center gap-1.5">
          <span className={cn('truncate text-sm font-medium', !inst.loggedIn && 'text-muted')}>{inst.loggedIn ? displayName(inst) : inst.name}</span>
        </span>
        <span className="mt-0.5 flex items-center gap-2 text-xs text-subtle">
          <span className="truncate">{sub}</span>
          {inst.status === 'running' && (inst.windowHidden ? <EyeOff className="size-3 shrink-0" aria-label="ซ่อนหน้าต่าง" /> : <Eye className="size-3 shrink-0" aria-label="แสดงหน้าต่าง" />)}
          {(inst.proxyId || inst.botProxy.enabled) && <Globe className="size-3 shrink-0" aria-label="ใช้ Proxy" />}
        </span>
      </span>
      <span className="shrink-0 text-right">
        {inst.captcha ? (
          <span className="inline-flex items-center gap-1 rounded-full bg-bad-soft px-2 py-0.5 text-xs font-medium text-bad">
            <ScanSearch className="size-3" aria-hidden />{inst.captcha.countdown}s
          </span>
        ) : inst.status === 'running' ? (
          <span className="text-sm font-medium tnum">{fmtNum(inst.stats.viewers)}<span className="block text-[11px] font-normal text-subtle">ผู้ชม</span></span>
        ) : (
          <span className="text-xs text-subtle">{meta.label}</span>
        )}
      </span>
    </button>
  )
})
