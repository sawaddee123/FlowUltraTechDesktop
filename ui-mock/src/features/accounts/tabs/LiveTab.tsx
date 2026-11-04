import { memo, useEffect, useState } from 'react'
import { ArrowDown, Gem, Radio } from 'lucide-react'
import type { LiveEvent } from '@/lib/types'
import { makeLiveEvent, seedLiveEvents } from '@/lib/mock'
import { useApp } from '@/lib/store'
import { fmtNum } from '@/lib/format'
import { cn } from '@/lib/cn'
import { Badge, Button, EmptyState, Skeleton } from '@/components/ui/base'
import type { TabProps } from './registry'
import { NeedRunning } from './tools/NeedRunning'
import { useFakeLoad, useStickBottom } from './tools/hooks'

const MAX_ROWS = 200

const Row = memo(function Row({ e }: { e: LiveEvent }) {
  return (
    <p className={cn('cv-auto flex items-baseline gap-2.5 rounded-ctl px-2.5 py-2 text-[15px] leading-normal', e.type !== 'chat' && 'text-muted')}>
      <span className="shrink-0 font-semibold text-muted">{e.nickname}</span>
      <span className="min-w-0 break-words">{e.message}</span>
      {e.gift && <span className="ml-auto inline-flex shrink-0 items-center gap-1 text-xs font-semibold text-warn tnum"><Gem className="size-3" aria-hidden />{fmtNum(e.gift.diamonds * e.gift.count)}</span>}
    </p>
  )
})

function Fact({ label, children }: { label: string; children: React.ReactNode }) {
  return <div className="flex items-center justify-between gap-3 text-sm"><span className="text-muted">{label}</span><span className="font-medium">{children}</span></div>
}

/** ภาพรวม: ตัวเลขสด + แชทสด + สิ่งที่โปรแกรมทำให้อัตโนมัติ */
export default function LiveTab({ instance: i }: TabProps) {
  const running = i.status === 'running'
  const ready = useFakeLoad()
  const proxy = useApp((s) => s.proxies.find((p) => p.id === i.proxyId))
  const solved = useApp((s) => s.history.filter((h) => h.instanceId === i.id && h.result === 'solved').length)
  const overlayOn = i.overlay.enabled
  const [events, setEvents] = useState<LiveEvent[]>(() => seedLiveEvents())
  const streaming = running && i.monitoring

  useEffect(() => {
    if (!streaming) return
    const id = window.setInterval(() => setEvents((l) => [...l, makeLiveEvent()].slice(-MAX_ROWS)), 1400)
    return () => window.clearInterval(id)
  }, [streaming])
  const follow = useStickBottom<HTMLDivElement>(`${ready}${events[events.length - 1]?.id}`)
  const st = i.stats

  return (
    <div className="flex flex-col gap-4">
      <NeedRunning instance={i} text="เริ่มไลฟ์เพื่อดูตัวเลขและแชทสด" />
      <section aria-label="ตัวเลขสด" className="grid grid-cols-[repeat(auto-fit,minmax(160px,1fr))] gap-3">
        {[['ผู้ชม', st.viewers], ['เพชร', st.diamonds], ['แชท', st.chats], ['ผู้ติดตามใหม่', st.follows]].map(([l, v]) => (
          <div key={l} className="rounded-card bg-surface px-4.5 py-4">
            <p className="text-sm text-muted">{l}</p>
            <p className="mt-1 text-[26px] font-semibold tracking-tight tnum">{running ? fmtNum(v as number) : '—'}</p>
          </div>
        ))}
      </section>

      <div className="flex flex-wrap items-start gap-4">
        <section aria-label="แชทสด" className="relative flex h-[440px] min-w-0 flex-[2_1_420px] flex-col rounded-card bg-surface px-3 pb-2 pt-4">
          <h2 className="mb-2 px-2.5 text-base font-semibold">แชทสด</h2>
          {!ready ? (
            <div className="flex flex-col gap-3 px-2.5" aria-busy="true" aria-label="กำลังโหลดแชท">
              {Array.from({ length: 8 }, (_, k) => <Skeleton key={k} className={cn('h-5', k % 3 ? 'w-2/3' : 'w-2/5')} />)}
            </div>
          ) : !running ? (
            <EmptyState icon={<Radio />} title="ยังไม่ได้ไลฟ์" hint="กด เริ่มไลฟ์ แล้วแชทจะขึ้นที่นี่เอง" />
          ) : (
            <div ref={follow.ref} onScroll={follow.onScroll} role="log" aria-label="แชทสด" tabIndex={0} className="scroll-y min-h-0 flex-1">
              {events.map((e) => <Row key={e.id} e={e} />)}
            </div>
          )}
          {ready && running && !follow.stuck && (
            <Button size="sm" className="absolute bottom-4 right-4 animate-rise shadow-float" icon={<ArrowDown className="size-3.5" />} onClick={follow.jump}>ล่าสุด</Button>
          )}
        </section>

        <section aria-label="ทำงานให้อัตโนมัติ" className="flex flex-[1_1_260px] flex-col gap-3.5 rounded-card bg-surface px-5 py-4.5">
          <h2 className="text-base font-semibold">ทำงานให้อัตโนมัติ</h2>
          <Fact label="Captcha">AI แก้ให้ {solved} ครั้ง</Fact>
          <Fact label="Proxy">{proxy ? `อัตโนมัติ · ${proxy.name}` : 'เชื่อมต่อตรง'}</Fact>
          <Fact label="นาฬิกาบนไลฟ์">{overlayOn ? 'เปิดอยู่' : 'ปิดอยู่'}</Fact>
          <Fact label="หน้าต่าง Studio">{i.windowHidden ? 'ซ่อนอยู่' : 'แสดงอยู่'}</Fact>
          <Fact label="บอทตอบแชท"><Badge>เร็วๆ นี้</Badge></Fact>
        </section>
      </div>
    </div>
  )
}
