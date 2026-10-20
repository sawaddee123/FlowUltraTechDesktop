import { memo, useEffect, useMemo, useState } from 'react'
import { ArrowDown, ChevronDown, Copy, FileText, FolderOpen, ScrollText, Search, SearchX, Trash2 } from 'lucide-react'
import type { LogEntry } from '@/lib/types'
import { seedLogs } from '@/lib/mock'
import { useApp } from '@/lib/store'
import { fmtTime } from '@/lib/format'
import { cn } from '@/lib/cn'
import { Button, EmptyState, Skeleton, StatusDot, toneClass, type Tone } from '@/components/ui/base'
import { Chip } from '@/components/ui/chip'
import { Input, Select } from '@/components/ui/form'
import { Menu } from '@/components/ui/overlay'
import type { TabProps } from './registry'
import { pick, useFakeLoad, useStickBottom } from './tools/hooks'

type Level = LogEntry['level']
const MAX_ROWS = 200
const LEVELS: { id: Level; label: string; tone: Tone }[] = [
  { id: 'info', label: 'info', tone: 'info' },
  { id: 'warn', label: 'warn', tone: 'warn' },
  { id: 'error', label: 'error', tone: 'bad' },
  { id: 'debug', label: 'debug', tone: 'neutral' },
]
const toneOf = Object.fromEntries(LEVELS.map((l) => [l.id, l.tone])) as Record<Level, Tone>

const LIVE_LINES: [Level, string, string][] = [
  ['info', 'Chat', 'รับแชทใหม่ 14 รายการ'],
  ['debug', 'CDP', 'Runtime.evaluate ใช้เวลา 18 ms'],
  ['info', 'Lease', 'ต่ออายุ Lease (เหลือ 9 นาที)'],
  ['warn', 'Network', 'webcast/room/enter ตอบช้า 2.1 วินาที'],
  ['info', 'Chat', 'Auto Reply: ตอบแชทตามกฎคำ'],
  ['debug', 'Window', 'ตรวจ HWND 0x0003A2C4 ยังอยู่'],
  ['error', 'Captcha', 'Solver ตอบ 502 — ลองใหม่ครั้งที่ 1'],
  ['info', 'Proxy', 'Bridge ส่งต่อข้อมูล 1.2 MB ใน 60 วินาที'],
  ['debug', 'Profile', 'อ่าน services.json สำเร็จ'],
  ['warn', 'CDP', 'WebSocket หลุด เชื่อมต่อใหม่ใน 2 วินาที'],
]

const GRID = 'grid grid-cols-[76px_52px_88px_minmax(0,1fr)] items-start gap-x-3 px-6'

const Row = memo(function Row({ e }: { e: LogEntry }) {
  return (
    <div className={cn(GRID, 'cv-auto py-1.5 transition-colors hover:bg-surface-2/60', e.level === 'error' && 'bg-bad-soft')}>
      <span className="pt-px text-xs tnum text-subtle">{fmtTime(e.at)}</span>
      <span className={cn('pt-px text-[11px] font-semibold uppercase', toneClass.text[toneOf[e.level]])}>{e.level}</span>
      <span className="truncate pt-px text-xs text-muted" title={e.category}>{e.category}</span>
      <span className="whitespace-pre-wrap break-words font-mono text-[13px] leading-snug text-fg">{e.message}</span>
    </div>
  )
})

function LogSkeleton() {
  return (
    <div className="flex flex-col gap-2 px-6 py-2" aria-busy="true" aria-label="กำลังโหลดบันทึก">
      {Array.from({ length: 10 }, (_, i) => (
        <div key={i} className="grid grid-cols-[76px_52px_88px_minmax(0,1fr)] items-start gap-x-3">
          <Skeleton className="h-4 w-14" /><Skeleton className="h-4 w-9" /><Skeleton className="h-4 w-16" /><Skeleton className={cn('h-4', i % 3 ? 'w-3/4' : 'w-1/2')} />
        </div>
      ))}
    </div>
  )
}

let seq = 0

export default function LogsTab({ instance }: TabProps) {
  const toast = useApp((s) => s.toast)
  const running = instance.status === 'running'
  const ready = useFakeLoad()
  const [logs, setLogs] = useState<LogEntry[]>(() => seedLogs(instance.name))
  const [levels, setLevels] = useState<Set<Level>>(() => new Set(LEVELS.map((l) => l.id)))
  const [cat, setCat] = useState('')
  const [q, setQ] = useState('')

  // บรรทัดใหม่จำลองทุก ~4 วินาทีเมื่อบัญชีทำงาน (เก็บ ≤ 200 แถว)
  useEffect(() => {
    if (!running) return
    const id = window.setInterval(() => {
      const [level, category, message] = pick(LIVE_LINES)
      setLogs((l) => [...l, { id: `live${Date.now().toString(36)}${seq++}`, at: Date.now(), level, category, message }].slice(-MAX_ROWS))
    }, 4000)
    return () => window.clearInterval(id)
  }, [running])

  const counts = useMemo(() => {
    const c: Record<Level, number> = { info: 0, warn: 0, error: 0, debug: 0 }
    logs.forEach((l) => c[l.level]++)
    return c
  }, [logs])
  const categories = useMemo(() => [...new Set(logs.map((l) => l.category))].sort(), [logs])

  const shown = useMemo(() => {
    const needle = q.trim().toLowerCase()
    return logs.filter((l) =>
      levels.has(l.level) && (!cat || l.category === cat) &&
      (!needle || l.message.toLowerCase().includes(needle) || l.category.toLowerCase().includes(needle)))
  }, [logs, levels, cat, q])

  const follow = useStickBottom<HTMLDivElement>(`${ready}${shown[shown.length - 1]?.id}${shown.length}`)

  const toggleLevel = (l: Level) => setLevels((s) => { const n = new Set(s); if (n.has(l)) n.delete(l); else n.add(l); return n })
  const filtered = levels.size < LEVELS.length || !!cat || !!q
  const resetFilters = () => { setLevels(new Set(LEVELS.map((l) => l.id))); setCat(''); setQ('') }
  const logPath = `%APPDATA%\\FlowUltraTech\\Logs\\${instance.folder}_debug.log`
  const copyAll = () => {
    const text = logs.map((l) => `[${fmtTime(l.at)}] ${l.level.toUpperCase()} ${l.category} ${l.message}`).join('\n')
    void navigator.clipboard?.writeText(text)
    toast({ tone: 'ok', title: 'คัดลอกบันทึกแล้ว', detail: `${logs.length} บรรทัด` })
  }

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="flex flex-wrap items-center gap-x-2 gap-y-2 px-6 py-3">
        <div className="relative w-52">
          <Search className="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-subtle" aria-hidden />
          <Input value={q} onChange={(e) => setQ(e.target.value)} placeholder="ค้นหาข้อความ" aria-label="ค้นหาบันทึก" className="pl-9" />
        </div>
        <div role="group" aria-label="กรองตามระดับ" className="flex flex-wrap gap-1.5">
          {LEVELS.map((l) => (
            <Chip key={l.id} pressed={levels.has(l.id)} onClick={() => toggleLevel(l.id)} tone={l.tone} dot count={counts[l.id]}>{l.label}</Chip>
          ))}
        </div>
        <div className="w-36"><Select value={cat} onChange={(e) => setCat(e.target.value)} aria-label="กรองตามหมวด">
          <option value="">ทุกหมวด</option>
          {categories.map((c) => <option key={c} value={c}>{c}</option>)}
        </Select></div>

        <div className="ml-auto flex items-center gap-2">
          <span className="flex items-center gap-1.5 text-xs text-subtle tnum">
            <StatusDot tone={running ? 'ok' : 'neutral'} pulse={running} />
            {running ? 'สด' : 'หยุด'} · {filtered ? `${shown.length}/${logs.length}` : logs.length} บรรทัด
          </span>
          <Button size="sm" icon={<Copy className="size-3.5" />} disabled={!logs.length} onClick={copyAll}>คัดลอกทั้งหมด</Button>
          <Button size="sm" variant="ghost" icon={<Trash2 className="size-3.5" />} disabled={!logs.length}
            onClick={() => { setLogs([]); toast({ tone: 'info', title: 'ล้างบันทึกแล้ว' }) }}>ล้าง</Button>
          <Menu
            width={220}
            items={[
              { label: 'เปิดไฟล์ Log', icon: <FileText />, onSelect: () => toast({ tone: 'info', title: 'เปิดไฟล์ Log', detail: logPath }) },
              { label: 'เปิดโฟลเดอร์ Log', icon: <FolderOpen />, onSelect: () => toast({ tone: 'info', title: 'เปิดโฟลเดอร์ Log', detail: '%APPDATA%\\FlowUltraTech\\Logs' }) },
            ]}
            trigger={({ toggle, open }) => (
              <Button size="sm" aria-haspopup="menu" aria-expanded={open} icon={<FolderOpen className="size-3.5" />} onClick={toggle}>
                เปิด<ChevronDown className="size-3.5 text-subtle" aria-hidden />
              </Button>
            )}
          />
        </div>
      </div>

      <div className="relative flex min-h-0 flex-1 flex-col">
        <div className={cn(GRID, 'shrink-0 bg-surface pb-1.5 text-xs font-medium text-subtle')} aria-hidden>
          <span>เวลา</span><span>ระดับ</span><span>หมวด</span><span>ข้อความ</span>
        </div>
        {!ready ? <LogSkeleton /> : shown.length === 0 ? (
          <div className="scroll-y min-h-0 flex-1">
            {logs.length === 0 ? (
              <EmptyState icon={<ScrollText />} title="ยังไม่มีบันทึก" hint={running ? 'บรรทัดใหม่จะขึ้นที่นี่อัตโนมัติ' : 'เริ่มบัญชีเพื่อให้บันทึกเกิดขึ้น'} />
            ) : (
              <EmptyState icon={<SearchX />} title="ไม่พบบรรทัดที่ตรงกับตัวกรอง" hint="ลองเปิดระดับเพิ่มหรือเปลี่ยนคำค้น"
                action={<Button size="sm" onClick={resetFilters}>ล้างตัวกรอง</Button>} />
            )}
          </div>
        ) : (
          <div ref={follow.ref} onScroll={follow.onScroll} role="log" aria-label="บันทึกระบบ" tabIndex={0} className="scroll-y min-h-0 flex-1 pb-2">
            {shown.map((e) => <Row key={e.id} e={e} />)}
          </div>
        )}
        {ready && !follow.stuck && shown.length > 0 && (
          <Button size="sm" className="absolute bottom-4 right-6 animate-rise shadow-float" icon={<ArrowDown className="size-3.5" />} onClick={follow.jump}>ล่าสุด</Button>
        )}
      </div>
    </div>
  )
}
