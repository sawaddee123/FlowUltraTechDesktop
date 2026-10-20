import { memo, useMemo, useState, type KeyboardEvent } from 'react'
import { Copy, Download, Eraser, Play, SquareTerminal } from 'lucide-react'
import { useApp } from '@/lib/store'
import { cn } from '@/lib/cn'
import { Button, EmptyState, IconButton, Kbd, Meter } from '@/components/ui/base'
import { Chip } from '@/components/ui/chip'
import { Textarea } from '@/components/ui/form'
import type { TabProps } from './registry'
import { NeedRunning } from './tools/NeedRunning'
import { useStickBottom, useTimers } from './tools/hooks'

const LIMIT = 64 * 1024

const QUICK = [
  { id: 'url', label: 'URL', code: 'location.href' },
  { id: 'title', label: 'Title', code: 'document.title' },
  { id: 'ls', label: 'LocalStorage', code: 'JSON.stringify(Object.fromEntries(Object.entries(localStorage)), null, 2)' },
  { id: 'html', label: 'Full HTML', code: 'document.documentElement.outerHTML' },
] as const

type Kind = 'in' | 'out' | 'err' | 'sys'
interface Line { id: number; kind: Kind; text: string }

const FULL_HTML = '<!DOCTYPE html>\n<html lang="th">\n<head>\n  <meta charset="utf-8">\n  <title>TikTok LIVE Studio</title>\n  <link rel="stylesheet" href="/obj/studio/main.css">\n</head>\n<body>\n  <div id="root">…</div>\n  <script src="/obj/studio/main.js"></script>\n</body>\n</html>'

/** จำลองผลลัพธ์ตามโค้ด — ไม่ได้รันจริง */
function evaluate(code: string, handle: string): { kind: Kind; text: string } {
  const thrown = code.match(/throw\s+(?:new\s+\w*Error\(\s*)?(['"`])(.*?)\1/)
  if (/\bthrow\b/.test(code)) return { kind: 'err', text: `Uncaught Error: ${thrown?.[2] ?? 'script error'}\n    at <anonymous>:1:1` }
  if (code.includes('outerHTML')) return { kind: 'out', text: FULL_HTML }
  if (code.includes('localStorage')) {
    return { kind: 'out', text: JSON.stringify({ 'tt-lang': 'th-TH', 'live-room-id': '7391820456123', 'studio-theme': 'dark', 'last-channel': handle }, null, 2) }
  }
  if (code.includes('document.title')) return { kind: 'out', text: 'TikTok LIVE Studio' }
  if (code.includes('location.href')) return { kind: 'out', text: `https://www.tiktok.com/@${handle || 'studio'}/live` }
  const log = code.match(/console\.log\(([\s\S]*)\)/)
  if (log) return { kind: 'out', text: log[1].trim().replace(/^(['"`])([\s\S]*)\1$/, '$2') }
  return { kind: 'out', text: 'undefined' }
}

const LineView = memo(function LineView({ l }: { l: Line }) {
  return (
    <div className={cn('cv-auto flex gap-2 py-0.5', l.kind === 'err' && 'text-bad', l.kind === 'in' && 'text-subtle', l.kind === 'sys' && 'text-subtle italic', l.kind === 'out' && 'text-fg')}>
      <span aria-hidden className="w-3 shrink-0 select-none text-subtle">{l.kind === 'in' ? '›' : l.kind === 'err' ? '×' : ''}</span>
      <span className="min-w-0 flex-1 whitespace-pre-wrap break-all">{l.text}</span>
    </div>
  )
})

let lineId = 0

export default function ScriptTab({ instance }: TabProps) {
  const toast = useApp((s) => s.toast)
  const later = useTimers()
  const running = instance.status === 'running'
  const [code, setCode] = useState<string>(QUICK[1].code)
  const [lines, setLines] = useState<Line[]>([])
  const [busy, setBusy] = useState(false)
  const [exporting, setExporting] = useState(false)

  const bytes = useMemo(() => lines.reduce((a, l) => a + l.text.length + 1, 0), [lines])
  const follow = useStickBottom<HTMLDivElement>(lines[lines.length - 1]?.id)

  const push = (add: { kind: Kind; text: string }[]) =>
    setLines((cur) => {
      const next = [...cur, ...add.map((a) => ({ ...a, id: lineId++ }))]
      let total = next.reduce((a, l) => a + l.text.length + 1, 0)
      let cut = 0
      while (total > LIMIT && cut < next.length - 1) total -= next[cut++].text.length + 1   // ตัดบรรทัดเก่าเมื่อเกิน 64 KB
      return cut ? next.slice(cut) : next
    })

  const run = () => {
    if (!running || busy || !code.trim()) return
    setBusy(true)
    push([{ kind: 'in', text: code.trim() }])
    later(() => {
      push([evaluate(code, instance.handle)])
      setBusy(false)
    }, 650)
  }
  const onKey = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') { e.preventDefault(); run() }
  }
  const exportHtml = () => {
    setExporting(true)
    later(() => {
      setExporting(false)
      toast({ tone: 'ok', title: 'ส่งออก Full HTML แล้ว', detail: `%APPDATA%\\FlowUltraTech\\Exports\\${instance.folder}_page.html` })
    }, 800)
  }
  const copyOut = () => {
    void navigator.clipboard?.writeText(lines.filter((l) => l.kind !== 'in').map((l) => l.text).join('\n'))
    toast({ tone: 'ok', title: 'คัดลอกผลลัพธ์แล้ว' })
  }

  return (
    <div className="flex h-full min-h-0 flex-col gap-3 px-6 py-4">
      {!running && <NeedRunning instance={instance} text="ต้องเริ่มบัญชีก่อนจึงรันสคริปต์ได้" />}

      <div className="flex shrink-0 flex-wrap items-center gap-2">
        <span className="mr-1 text-[13px] font-medium text-muted">คำสั่งด่วน</span>
        {QUICK.map((q) => <Chip key={q.id} onClick={() => setCode(q.code)}>{q.label}</Chip>)}
        <div className="ml-auto flex items-center gap-2.5">
          <span className="hidden items-center gap-1 text-xs text-subtle sm:flex"><Kbd>Ctrl</Kbd>+<Kbd>Enter</Kbd></span>
          <Button variant="primary" className="min-w-24" loading={busy} disabled={!running || !code.trim()}
            title={running ? undefined : 'ต้องเริ่มบัญชีก่อน'} icon={<Play className="size-3.5 fill-current" />} onClick={run}>
            {busy ? 'กำลังรัน' : 'รัน'}
          </Button>
        </div>
      </div>

      <Textarea
        mono value={code} onChange={(e) => setCode(e.target.value)} onKeyDown={onKey} spellCheck={false}
        aria-label="โค้ด JavaScript" placeholder="เขียน JavaScript ที่จะรันในหน้า Studio" className="min-h-24 flex-[2] resize-none"
      />

      <section aria-label="Console" className="flex min-h-0 flex-[3] flex-col rounded-card bg-surface-2/60">
        <header className="flex shrink-0 items-center gap-2 px-4 pb-1 pt-2.5">
          <h3 className="text-[13px] font-semibold uppercase tracking-wide text-subtle">Console</h3>
          <div className="flex w-28 items-center gap-2" title={`${(bytes / 1024).toFixed(1)} / 64 KB`}>
            <Meter value={bytes} max={LIMIT} tone={bytes > LIMIT * 0.85 ? 'warn' : 'info'} className="flex-1" />
          </div>
          <span className="text-xs text-subtle tnum">{(bytes / 1024).toFixed(1)} / 64 KB</span>
          <div className="ml-auto flex items-center gap-1">
            <Button size="sm" variant="ghost" loading={exporting} disabled={!running} icon={<Download className="size-3.5" />} onClick={exportHtml} className="min-w-[8.5rem]">ส่งออก Full HTML</Button>
            <IconButton label="คัดลอกผลลัพธ์" size="sm" disabled={!lines.length} onClick={copyOut}><Copy className="size-4" /></IconButton>
            <IconButton label="ล้าง Console" size="sm" disabled={!lines.length} onClick={() => setLines([])}><Eraser className="size-4" /></IconButton>
          </div>
        </header>
        <div ref={follow.ref} onScroll={follow.onScroll} role="log" aria-label="ผลลัพธ์สคริปต์" tabIndex={0}
          className="scroll-y min-h-0 flex-1 px-4 pb-3 font-mono text-[13px] leading-relaxed">
          {lines.length === 0 ? (
            <EmptyState icon={<SquareTerminal />} title="ยังไม่มีผลลัพธ์" hint="เลือกคำสั่งด่วนหรือเขียนโค้ด แล้วกดรัน" />
          ) : lines.map((l) => <LineView key={l.id} l={l} />)}
        </div>
      </section>
    </div>
  )
}
