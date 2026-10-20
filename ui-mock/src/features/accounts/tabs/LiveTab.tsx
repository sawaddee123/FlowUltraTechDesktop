import { memo, useEffect, useMemo, useState } from 'react'
import { ArrowDown, Bot, Gem, Heart, LogIn, MessageCircle, Pause, Play, Radio, Share2, Trash2, UserPlus } from 'lucide-react'
import type { LiveEvent, LiveEventType } from '@/lib/types'
import { makeLiveEvent, seedLiveEvents } from '@/lib/mock'
import { useApp } from '@/lib/store'
import { fmtNum, fmtTime } from '@/lib/format'
import { cn } from '@/lib/cn'
import { Button, EmptyState, Skeleton, Stat, StatusDot, Switch, toneClass, type Tone } from '@/components/ui/base'
import { Chip } from '@/components/ui/chip'
import { Input } from '@/components/ui/form'
import type { TabProps } from './registry'
import { NeedRunning } from './tools/NeedRunning'
import { useFakeLoad, useStickBottom } from './tools/hooks'

const MAX_ROWS = 200
const TYPES: { id: LiveEventType; label: string; tone: Tone; icon: typeof MessageCircle }[] = [
  { id: 'chat', label: 'แชท', tone: 'info', icon: MessageCircle },
  { id: 'gift', label: 'ของขวัญ', tone: 'warn', icon: Gem },
  { id: 'like', label: 'ถูกใจ', tone: 'neutral', icon: Heart },
  { id: 'follow', label: 'ติดตาม', tone: 'ok', icon: UserPlus },
  { id: 'share', label: 'แชร์', tone: 'neutral', icon: Share2 },
  { id: 'join', label: 'เข้าห้อง', tone: 'neutral', icon: LogIn },
  { id: 'bot', label: 'บอทตอบ', tone: 'ok', icon: Bot },
]
const META = Object.fromEntries(TYPES.map((t) => [t.id, t])) as Record<LiveEventType, (typeof TYPES)[number]>

const Row = memo(function Row({ e }: { e: LiveEvent }) {
  const m = META[e.type]
  const Icon = m.icon
  return (
    <div className="cv-auto grid grid-cols-[64px_20px_minmax(0,1fr)] items-start gap-x-3 px-6 py-1.5 transition-colors hover:bg-surface-2/60">
      <span className="pt-px text-xs tnum text-subtle">{fmtTime(e.at)}</span>
      <span className={cn('pt-0.5', toneClass.text[m.tone])} title={m.label}>
        <Icon className="size-4" aria-label={m.label} />
      </span>
      <div className="min-w-0 text-sm">
        <p className="break-words">
          <span className="font-medium text-fg">{e.nickname}</span>{' '}
          <span className="text-muted">{e.message}</span>
          {e.gift && (
            <span className={cn('ml-2 inline-flex items-center gap-1 text-xs font-semibold tnum', toneClass.text.warn)}>
              <Gem className="size-3" aria-hidden />{fmtNum(e.gift.diamonds * e.gift.count)}
            </span>
          )}
        </p>
        {e.replied && (
          <p className={cn('mt-0.5 flex items-start gap-1.5 text-[13px]', toneClass.text.ok)}>
            <Bot className="mt-0.5 size-3.5 shrink-0" aria-hidden /><span className="break-words">{e.replied}</span>
          </p>
        )}
      </div>
    </div>
  )
})

function FeedSkeleton() {
  return (
    <div className="flex flex-col gap-2.5 px-6 py-2" aria-busy="true" aria-label="กำลังโหลดฟีด">
      {Array.from({ length: 9 }, (_, i) => (
        <div key={i} className="grid grid-cols-[64px_20px_minmax(0,1fr)] gap-x-3">
          <Skeleton className="h-4 w-12" /><Skeleton className="size-4" /><Skeleton className={cn('h-4', i % 3 ? 'w-2/3' : 'w-2/5')} />
        </div>
      ))}
    </div>
  )
}

export default function LiveTab({ instance }: TabProps) {
  const patch = useApp((s) => s.patchInstance)
  const toast = useApp((s) => s.toast)
  const running = instance.status === 'running'
  const ready = useFakeLoad()
  const [events, setEvents] = useState<LiveEvent[]>(() => seedLiveEvents())
  const [paused, setPaused] = useState(false)
  const [active, setActive] = useState<Set<LiveEventType>>(() => new Set(TYPES.map((t) => t.id)))
  const [channel, setChannel] = useState(instance.channel)
  const streaming = running && instance.monitoring && !paused

  useEffect(() => { setChannel(instance.channel) }, [instance.channel])

  useEffect(() => {
    if (!streaming) return
    const id = window.setInterval(() => {
      setEvents((l) => [...l, makeLiveEvent()].slice(-MAX_ROWS))
    }, 1400)
    return () => window.clearInterval(id)
  }, [streaming])

  const counts = useMemo(() => {
    const c = {} as Record<LiveEventType, number>
    TYPES.forEach((t) => { c[t.id] = 0 })
    events.forEach((e) => c[e.type]++)
    return c
  }, [events])
  const shown = useMemo(() => events.filter((e) => active.has(e.type)), [events, active])
  const follow = useStickBottom<HTMLDivElement>(`${ready}${shown[shown.length - 1]?.id}${shown.length}`)

  const toggle = (t: LiveEventType) => setActive((s) => { const n = new Set(s); if (n.has(t)) n.delete(t); else n.add(t); return n })
  const commitChannel = () => {
    const v = channel.trim().replace(/^@/, '')
    if (v && v !== instance.channel) patch(instance.id, { channel: v })
    else setChannel(instance.channel)
  }
  const st = instance.stats
  const state = !running ? 'หยุด' : !instance.monitoring ? 'ปิดมอนิเตอร์' : paused ? 'พักฟีด' : 'สด'

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="flex flex-col gap-3 px-6 pt-4">
        <NeedRunning instance={instance} text="เริ่มบัญชีเพื่อรับแชทและกิจกรรมสดจากห้องไลฟ์" />
        <div className="grid grid-cols-3 gap-x-6 gap-y-3 sm:grid-cols-4 lg:grid-cols-7" aria-label="สถิติไลฟ์">
          <Stat label="ผู้ชม" value={fmtNum(st.viewers)} />
          <Stat label="แชท" value={fmtNum(st.chats)} />
          <Stat label="ของขวัญ" value={fmtNum(st.gifts)} />
          <Stat label="เพชร" value={fmtNum(st.diamonds)} tone="warn" />
          <Stat label="ถูกใจ" value={fmtNum(st.likes)} />
          <Stat label="ผู้ติดตามใหม่" value={fmtNum(st.follows)} />
          <Stat label="บอทตอบ" value={fmtNum(st.botReplies)} tone="ok" />
        </div>
        <div className="flex flex-wrap items-center gap-x-4 gap-y-2">
          <div className="flex items-center gap-2">
            <Switch checked={instance.monitoring} onChange={(v) => patch(instance.id, { monitoring: v })} label="มอนิเตอร์แชท" />
            <span className="text-sm text-muted">มอนิเตอร์แชท</span>
          </div>
          <div className="relative w-60">
            <span className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-sm text-subtle">@</span>
            <Input value={channel.replace(/^@/, '')} onChange={(e) => setChannel(e.target.value)} onBlur={commitChannel}
              onKeyDown={(e) => { if (e.key === 'Enter') e.currentTarget.blur() }}
              placeholder="ชื่อช่อง" aria-label="ช่องที่มอนิเตอร์" className="pl-7" />
          </div>
          <span className="ml-auto flex items-center gap-1.5 text-xs text-subtle tnum">
            <StatusDot tone={streaming ? 'ok' : 'neutral'} pulse={streaming} />{state} · {events.length} รายการ
          </span>
          <Button size="sm" icon={paused ? <Play className="size-3.5 fill-current" /> : <Pause className="size-3.5" />}
            disabled={!running || !instance.monitoring} onClick={() => setPaused((p) => !p)}>{paused ? 'เล่นต่อ' : 'พัก'}</Button>
          <Button size="sm" variant="ghost" icon={<Trash2 className="size-3.5" />} disabled={!events.length}
            onClick={() => { setEvents([]); toast({ tone: 'info', title: 'ล้างฟีดแล้ว' }) }}>ล้าง</Button>
        </div>
        <div role="group" aria-label="กรองตามประเภท" className="flex flex-wrap gap-1.5 pb-3">
          {TYPES.map((t) => (
            <Chip key={t.id} pressed={active.has(t.id)} onClick={() => toggle(t.id)} tone={t.tone} dot count={counts[t.id]}>{t.label}</Chip>
          ))}
        </div>
      </div>

      <div className="relative flex min-h-0 flex-1 flex-col">
        {!ready ? <FeedSkeleton /> : shown.length === 0 ? (
          <div className="scroll-y min-h-0 flex-1">
            <EmptyState icon={<Radio />} title={events.length ? 'ไม่มีรายการในประเภทที่เลือก' : 'ยังไม่มีกิจกรรม'}
              hint={events.length ? 'เปิดประเภทเพิ่มจากชิปด้านบน' : running ? 'เปิดมอนิเตอร์แชทเพื่อรับกิจกรรมสด' : 'เริ่มบัญชีแล้วเปิดมอนิเตอร์แชท'}
              action={events.length ? <Button size="sm" onClick={() => setActive(new Set(TYPES.map((t) => t.id)))}>เปิดทุกประเภท</Button> : undefined} />
          </div>
        ) : (
          <div ref={follow.ref} onScroll={follow.onScroll} role="log" aria-label="ฟีดกิจกรรมสด" tabIndex={0} className="scroll-y min-h-0 flex-1 pb-2">
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
