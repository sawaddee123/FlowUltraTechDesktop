import { useState } from 'react'
import { Copy, Eye, EyeOff, RotateCw } from 'lucide-react'
import type { Instance } from '@/lib/types'
import { useApp } from '@/lib/store'
import { fmtTime } from '@/lib/format'
import { Button, CopyButton, IconButton, Skeleton } from '@/components/ui/base'
import { Section } from '@/components/ui/layout'
import { cn } from '@/lib/cn'
import { useTimers } from './hooks'

const HEX = '0123456789abcdef'
const MIXED = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789'

/** ค่าจำลองที่คงที่ตามบัญชีและรอบที่ดึง */
function fakeToken(seed: string, len: number, alphabet: string) {
  let h = 2166136261
  for (const c of seed) h = Math.imul(h ^ c.charCodeAt(0), 16777619)
  let out = ''
  for (let k = 0; k < len; k++) {
    h = (Math.imul(h ^ (h >>> 15), 2246822507) + k) | 0
    out += alphabet[(h >>> 0) % alphabet.length]
  }
  return out
}

const mask = (v: string) => `${v.slice(0, 4)}${'•'.repeat(14)}${v.slice(-4)}`

export function TokensSection({ instance: i }: { instance: Instance }) {
  const toast = useApp((s) => s.toast)
  const later = useTimers()
  const running = i.status === 'running'
  const [round, setRound] = useState({ v: 0, at: Date.now() })
  const [busy, setBusy] = useState(false)
  const [shown, setShown] = useState<Record<string, boolean>>({})

  const rows = [
    { key: 'sessionid', value: fakeToken(`${i.id}:sid:${round.v}`, 32, HEX) },
    { key: 'tt_csrf_token', value: fakeToken(`${i.id}:csrf:${round.v}`, 24, MIXED) },
  ]
  const refetch = () => {
    setBusy(true)
    later(() => { setRound((r) => ({ v: r.v + 1, at: Date.now() })); setBusy(false); toast({ tone: 'ok', title: 'ดึง Cookies ใหม่แล้ว', detail: 'sessionid, tt_csrf_token' }) }, 900)
  }
  const why = 'ต้องเริ่มบัญชีก่อน'

  return (
    <Section
      title="Cookies & Tokens"
      aside={
        <div className="flex items-center gap-3">
          <span className={cn('text-xs tnum', running ? 'text-subtle' : 'text-warn')}>{running ? `ดึงเมื่อ ${fmtTime(round.at)}` : why}</span>
          <Button size="sm" className="min-w-24" loading={busy} disabled={!running} title={running ? undefined : why} icon={<RotateCw className="size-3.5" />} onClick={refetch}>ดึงใหม่</Button>
        </div>
      }
    >
      <div className="flex flex-col gap-2.5">
        {rows.map((r) => {
          const open = !!shown[r.key]
          return (
            <div key={r.key} className="grid grid-cols-[112px_minmax(0,1fr)_auto] items-center gap-2">
              <span className="truncate font-mono text-[13px] text-muted">{r.key}</span>
              {busy ? <Skeleton className="h-9 w-full rounded-ctl" /> : (
                <div className={cn('flex h-9 items-center rounded-ctl bg-surface-2 px-3 font-mono text-[13px]', running ? 'text-fg' : 'text-subtle')}>
                  <span className="min-w-0 flex-1 truncate">{running ? (open ? r.value : mask(r.value)) : '—'}</span>
                </div>
              )}
              <div className="flex items-center">
                <IconButton label={open ? `ซ่อน ${r.key}` : `แสดง ${r.key}`} size="sm" disabled={!running || busy} aria-pressed={open}
                  onClick={() => setShown((s) => ({ ...s, [r.key]: !s[r.key] }))}>
                  {open ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
                </IconButton>
                {running && !busy
                  ? <CopyButton text={r.value} label={`คัดลอก ${r.key}`} />
                  : <IconButton label={`คัดลอก ${r.key}`} size="sm" disabled tip={false}><Copy className="size-4" /></IconButton>}
              </div>
            </div>
          )
        })}
      </div>
    </Section>
  )
}
