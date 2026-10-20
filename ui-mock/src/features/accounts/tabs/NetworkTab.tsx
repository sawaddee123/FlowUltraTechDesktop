import { memo, useEffect, useMemo, useState, type KeyboardEvent } from 'react'
import { Network, Play, Search, SearchX, Trash2, X } from 'lucide-react'
import type { NetworkEntry } from '@/lib/types'
import { seedNetwork } from '@/lib/mock'
import { fmtTime } from '@/lib/format'
import { cn } from '@/lib/cn'
import { Badge, Button, CopyButton, EmptyState, IconButton, Skeleton, Switch, type Tone } from '@/components/ui/base'
import { Input } from '@/components/ui/form'
import { DataTable, tdc } from '@/components/ui/layout'
import type { TabProps } from './registry'
import { NeedRunning } from './tools/NeedRunning'
import { JsonView } from './tools/JsonView'
import { pick, useFakeLoad } from './tools/hooks'

const MAX_ROWS = 200
const statusTone = (s: number): Tone => (s >= 500 ? 'bad' : s >= 400 ? 'warn' : s >= 300 ? 'info' : 'ok')

const LIVE: [string, number, string, string, number][] = [
  ['GET', 200, 'fetch', 'https://webcast.tiktok.com/webcast/im/fetch/?aid=1988', 220],
  ['POST', 200, 'xhr', 'https://webcast.tiktok.com/webcast/room/chat/', 164],
  ['GET', 200, 'image', 'https://p16-sign.example/gift/rose.png', 48],
  ['GET', 304, 'script', 'https://lf16-tiktok-web.example/obj/studio/chunk-2.js', 19],
  ['POST', 429, 'xhr', 'https://webcast.tiktok.com/webcast/room/chat/', 92],
  ['GET', 200, 'fetch', 'https://mssdk.tiktok.com/web/report', 133],
  ['GET', 502, 'fetch', 'https://webcast.tiktok.com/webcast/room/info/', 1800],
  ['GET', 301, 'document', 'https://www.tiktok.com/live', 74],
]

let seq = 0
const bodyFor = (status: number) =>
  JSON.stringify(
    status >= 500 ? { status_code: 50002, message: 'upstream error' }
      : status === 429 ? { status_code: 20003, message: 'rate limited', retry_after: 3 }
        : status >= 300 ? { redirect: true, location: 'https://www.tiktok.com/live' }
          : { status_code: 0, data: { room_id: '7391820456123', viewers: Math.round(180 + Math.random() * 300), ok: true } },
    null, 2)

const makeEntry = (): NetworkEntry => {
  const [method, status, type, url, ms] = pick(LIVE)
  return { id: `nl${Date.now().toString(36)}${seq++}`, at: Date.now(), method, status, type, url, ms: ms + Math.round(Math.random() * 40), body: bodyFor(status) }
}

/** คงสถานะ Hook และรายการไว้ข้ามการสลับแท็บ */
const cache = new Map<string, { on: boolean; rows: NetworkEntry[] }>()

const Row = memo(function Row({ e, selected, onSelect, onMove }: {
  e: NetworkEntry; selected: boolean; onSelect: (id: string) => void; onMove: (el: HTMLElement, dir: 1 | -1) => void
}) {
  const onKey = (ev: KeyboardEvent<HTMLTableRowElement>) => {
    if (ev.key === 'Enter' || ev.key === ' ') { ev.preventDefault(); onSelect(e.id) }
    else if (ev.key === 'ArrowDown' || ev.key === 'ArrowUp') { ev.preventDefault(); onMove(ev.currentTarget, ev.key === 'ArrowDown' ? 1 : -1) }
  }
  return (
    <tr
      tabIndex={0} aria-selected={selected} onClick={() => onSelect(e.id)} onKeyDown={onKey}
      className={cn('cv-auto cursor-pointer transition-colors', selected ? 'bg-primary-soft' : 'hover:bg-surface-2/60')}
    >
      <td className={cn(tdc, 'text-xs text-subtle tnum')}>{fmtTime(e.at)}</td>
      <td className={cn(tdc, 'text-xs font-semibold text-muted')}>{e.method}</td>
      <td className={tdc}><Badge tone={statusTone(e.status)}>{e.status}</Badge></td>
      <td className={cn(tdc, 'text-xs text-muted')}>{e.type}</td>
      <td className={cn(tdc, 'max-w-0')}><span className="block truncate font-mono text-[13px]" title={e.url}>{e.url}</span></td>
      <td className={cn(tdc, 'text-right text-xs text-subtle tnum')}>{e.ms || '—'}</td>
    </tr>
  )
})

const HEAD = [
  { label: 'เวลา', className: 'w-[86px]' }, { label: 'Method', className: 'w-[72px]' }, { label: 'Status', className: 'w-[76px]' },
  { label: 'Type', className: 'w-[80px]' }, { label: 'URL' }, { label: 'ms', className: 'w-[64px] text-right' },
]

function TableSkeleton() {
  return (
    <div className="flex flex-col gap-2.5 px-4 py-3" aria-busy="true" aria-label="กำลังโหลดรายการ">
      {Array.from({ length: 8 }, (_, i) => <Skeleton key={i} className="h-6 w-full" />)}
    </div>
  )
}

export default function NetworkTab({ instance }: TabProps) {
  const running = instance.status === 'running'
  const ready = useFakeLoad()
  const [on, setOn] = useState(() => cache.get(instance.id)?.on ?? false)
  const [rows, setRows] = useState<NetworkEntry[]>(() => cache.get(instance.id)?.rows ?? [])
  const [sel, setSel] = useState<string | null>(null)
  const [q, setQ] = useState('')

  useEffect(() => { cache.set(instance.id, { on, rows }) }, [instance.id, on, rows])
  useEffect(() => { if (!running) setOn(false) }, [running])

  const enable = (v: boolean) => {
    setOn(v)
    if (v && rows.length === 0) setRows(seedNetwork().reverse())
  }

  // รายการใหม่จำลองเมื่อสวิตช์เปิดและบัญชีทำงาน (ใหม่สุดอยู่บน เก็บ ≤ 200 แถว)
  useEffect(() => {
    if (!on || !running) return
    const id = window.setInterval(() => setRows((r) => [makeEntry(), ...r].slice(0, MAX_ROWS)), 3500)
    return () => window.clearInterval(id)
  }, [on, running])

  const shown = useMemo(() => {
    const n = q.trim().toLowerCase()
    return n ? rows.filter((r) => `${r.method} ${r.status} ${r.type} ${r.url}`.toLowerCase().includes(n)) : rows
  }, [rows, q])
  const current = rows.find((r) => r.id === sel) ?? null

  const move = (el: HTMLElement, dir: 1 | -1) => {
    const next = (dir === 1 ? el.nextElementSibling : el.previousElementSibling) as HTMLElement | null
    next?.focus()
    next?.click()
  }

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 px-6 py-3">
        <label className="flex items-center gap-2.5 text-sm font-medium">
          <Switch checked={on} onChange={enable} label="Network Hook" disabled={!running} />
          Network Hook
        </label>
        <div className="relative w-64">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-subtle" aria-hidden />
          <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder="ค้นหา URL, Method, Status" aria-label="ค้นหาคำขอ" className="pl-9" />
        </div>
        <div className="ml-auto flex items-center gap-3">
          <span className="text-xs text-subtle tnum">{q ? `${shown.length}/${rows.length}` : rows.length} รายการ</span>
          <Button size="sm" variant="ghost" icon={<Trash2 className="size-3.5" />} disabled={!rows.length} onClick={() => { setRows([]); setSel(null) }}>ล้าง</Button>
        </div>
      </div>
      {!running && <div className="px-6 pb-3"><NeedRunning instance={instance} text="ต้องเริ่มบัญชีก่อนจึงเปิด Network Hook ได้" /></div>}

      {!on && rows.length === 0 ? (
        <EmptyState
          icon={<Network />} title="ยังไม่ได้ดักคำขอ"
          hint={running ? 'เปิด Network Hook เพื่อดู API ที่ Studio เรียก พร้อม Response Body' : 'เริ่มบัญชีแล้วเปิด Network Hook'}
          action={running ? <Button icon={<Play className="size-3.5 fill-current" />} onClick={() => enable(true)}>เปิด Hook</Button> : undefined}
        />
      ) : (
        <div className="@container min-h-0 flex-1">
          <div className="flex h-full min-h-0 flex-col @3xl:flex-row">
            <div className="scroll-y min-h-0 flex-1 @3xl:basis-3/5">
              {!ready ? <TableSkeleton /> : shown.length === 0 ? (
                rows.length === 0 ? (
                  <EmptyState icon={<Network />} title="รอคำขอแรก" hint="คำขอใหม่จะขึ้นที่แถวบนสุดอัตโนมัติ" />
                ) : (
                  <EmptyState icon={<SearchX />} title="ไม่พบคำขอที่ตรงกับคำค้น" action={<Button size="sm" onClick={() => setQ('')}>ล้างคำค้น</Button>} />
                )
              ) : (
                <DataTable className="table-fixed" head={HEAD}>
                  {shown.map((e) => <Row key={e.id} e={e} selected={e.id === sel} onSelect={setSel} onMove={move} />)}
                </DataTable>
              )}
            </div>

            <aside aria-label="Response Body" className="flex min-h-0 flex-1 flex-col bg-surface-2/50 @3xl:basis-2/5">
              {current ? (
                <>
                  <div className="flex shrink-0 items-start gap-3 px-4 pb-2 pt-3">
                    <div className="min-w-0 flex-1">
                      <div className="flex items-center gap-2">
                        <span className="text-xs font-semibold text-muted">{current.method}</span>
                        <Badge tone={statusTone(current.status)}>{current.status}</Badge>
                        <span className="text-xs text-subtle tnum">{current.ms || '—'} ms · {current.type}</span>
                      </div>
                      <p className="mt-1 break-all font-mono text-xs text-muted">{current.url}</p>
                    </div>
                    <CopyButton text={current.body} label="คัดลอก Response Body" />
                    <IconButton label="ปิดรายละเอียด" size="sm" onClick={() => setSel(null)}><X className="size-4" /></IconButton>
                  </div>
                  <div className="scroll-y min-h-0 flex-1 px-4 pb-4"><JsonView text={current.body} /></div>
                </>
              ) : (
                <EmptyState icon={<Network />} title="เลือกคำขอเพื่อดู Response" hint="กดแถวในตาราง หรือใช้ลูกศรขึ้น-ลง" />
              )}
            </aside>
          </div>
        </div>
      )}
      <span className="sr-only" aria-live="polite">{on ? 'Network Hook เปิดอยู่' : 'Network Hook ปิดอยู่'}</span>
    </div>
  )
}
