import { useParams } from 'react-router-dom'
import { CircleAlert, Play, Plus, Radio, RotateCw, Square, TriangleAlert } from 'lucide-react'
import { useShallow } from 'zustand/react/shallow'
import { useApp } from '@/lib/store'
import { displayName, fmtNum, needsHelp } from '@/lib/format'
import { Button } from '@/components/ui/base'
import { AccountCard } from './AccountCard'
import { AccountDetail } from './AccountDetail'
import { AdvancedPage } from './AdvancedPage'

function Welcome() {
  const setAddOpen = useApp((s) => s.setAddOpen)
  const steps = ['เพิ่มบัญชี แล้วล็อกอิน TikTok', 'กด เริ่มไลฟ์', 'AI ดูแล Captcha ให้ตลอดไลฟ์']
  return (
    <div className="grid min-h-full place-items-center px-6 py-12">
      <div className="flex max-w-xl animate-rise flex-col items-center gap-7 text-center">
        <span className="grid size-[72px] place-items-center rounded-[22px] bg-primary-soft text-primary"><Radio className="size-8" aria-hidden /></span>
        <div>
          <h1 className="text-3xl font-semibold tracking-tight">ยินดีต้อนรับ</h1>
          <p className="mt-2 text-base text-muted">เพิ่มบัญชี TikTok แรกของคุณเพื่อเริ่มไลฟ์ ใช้เวลาไม่ถึงนาที</p>
        </div>
        <Button variant="primary" className="h-13 px-7 text-base" icon={<Plus className="size-5" />} onClick={() => setAddOpen(true)}>เพิ่มบัญชีแรก</Button>
        <ol className="mt-2 grid w-full grid-cols-3 gap-3">
          {steps.map((t, k) => (
            <li key={t} className="flex flex-col items-center gap-2 rounded-card bg-surface px-3 py-5 text-sm">
              <span className="grid size-7 place-items-center rounded-full bg-surface-3 font-semibold">{k + 1}</span>{t}
            </li>
          ))}
        </ol>
      </div>
    </div>
  )
}

function Home() {
  const { instances, autoSolve, solvedToday } = useApp(useShallow((s) => ({
    instances: s.instances, autoSolve: s.settings.autoSolve,
    solvedToday: s.history.filter((h) => h.result === 'solved' && h.mode !== 'manual' && Date.now() - h.at < 86_400_000).length,
  })))
  const { startAll, stopAll, start, setAddOpen, setHelpId } = useApp(useShallow((s) => ({
    startAll: s.startAll, stopAll: s.stopAll, start: s.startInstance, setAddOpen: s.setAddOpen, setHelpId: s.setHelpId,
  })))

  if (!instances.length) return <Welcome />

  const live = instances.filter((i) => i.status === 'running')
  const startable = instances.filter((i) => i.loggedIn && i.status !== 'running' && i.status !== 'starting')
  const help = instances.filter((i) => needsHelp(i, autoSolve))
  const failed = instances.filter((i) => i.status === 'error')
  const sum = (k: 'viewers' | 'diamonds') => live.reduce((n, i) => n + i.stats[k], 0)

  return (
    <div className="flex flex-col gap-6 px-10 pb-12 pt-8 max-[900px]:px-5">
      <header className="flex flex-wrap items-center justify-between gap-4">
        <div>
          <h1 className="text-[26px] font-semibold tracking-tight">บัญชีของคุณ</h1>
          <p className="mt-1 text-[15px] text-muted">กำลังไลฟ์ {live.length} จาก {instances.length} บัญชี</p>
        </div>
        {startable.length > 0 ? (
          <Button variant="primary" className="h-12 px-6 text-base" icon={<Play className="size-4 fill-current" />} onClick={startAll}>เริ่มทั้งหมด</Button>
        ) : (
          <Button className="h-12 px-6 text-base" icon={<Square className="size-4 fill-current" />} disabled={!live.length} onClick={stopAll}>หยุดทั้งหมด</Button>
        )}
      </header>

      {(help.length > 0 || failed.length > 0) && (
        <div className="flex flex-col gap-2">
          {help.map((i) => (
            <div key={i.id} role="status" className="flex animate-rise flex-wrap items-center gap-3.5 rounded-card bg-warn-soft px-4.5 py-3.5">
              <TriangleAlert className="size-5 shrink-0 text-warn" aria-hidden />
              <div className="min-w-0 flex-[1_1_280px]">
                <p className="font-semibold">{displayName(i)} ต้องให้คุณช่วยแก้ Captcha</p>
                <p className="text-sm text-muted">{i.captcha?.aiFailed ? 'AI ลองแก้ให้แล้วไม่ผ่าน' : 'ปิดแก้อัตโนมัติไว้'} · ไลฟ์ยังเปิดอยู่ · เหลือ <span className="tnum">{i.captcha?.countdown}</span> วินาที</p>
              </div>
              <Button className="h-11 bg-warn px-5 text-[15px] text-on-primary hover:bg-warn hover:opacity-90" onClick={() => setHelpId(i.id)}>แก้ตอนนี้</Button>
            </div>
          ))}
          {failed.map((i) => (
            <div key={i.id} role="alert" className="flex animate-rise flex-wrap items-center gap-3.5 rounded-card bg-bad-soft px-4.5 py-3.5">
              <CircleAlert className="size-5 shrink-0 text-bad" aria-hidden />
              <div className="min-w-0 flex-[1_1_280px]">
                <p className="font-semibold">{displayName(i)} เริ่มไม่สำเร็จ</p>
                <p className="truncate text-sm text-muted" title={i.error}>{i.error}</p>
              </div>
              <Button variant="danger" className="h-11 px-5 text-[15px]" icon={<RotateCw className="size-4" />} onClick={() => start(i.id)}>ลองใหม่</Button>
            </div>
          ))}
        </div>
      )}

      <section aria-label="สรุปวันนี้" className="grid grid-cols-3 gap-4 max-[700px]:grid-cols-1">
        {[
          { label: 'ผู้ชมตอนนี้', value: fmtNum(sum('viewers')) },
          { label: 'เพชรวันนี้', value: fmtNum(sum('diamonds')) },
          { label: 'Captcha ที่ AI แก้ให้', value: fmtNum(solvedToday) },
        ].map((s) => (
          <div key={s.label} className="rounded-card bg-surface px-5 py-4.5">
            <p className="text-sm text-muted">{s.label}</p>
            <p className="mt-1.5 text-3xl font-semibold tracking-tight tnum">{s.value}</p>
          </div>
        ))}
      </section>

      <section aria-label="รายการบัญชี" className="grid grid-cols-[repeat(auto-fill,minmax(290px,1fr))] gap-4">
        {instances.map((i) => <AccountCard key={i.id} instance={i} />)}
        <button type="button" onClick={() => setAddOpen(true)}
          className="flex min-h-[220px] flex-col items-center justify-center gap-2.5 rounded-card border-2 border-dashed border-line text-[15px] font-medium text-muted transition-colors hover:border-primary hover:text-fg">
          <span className="grid size-11 place-items-center rounded-full bg-primary-soft text-primary"><Plus className="size-5" aria-hidden /></span>
          เพิ่มบัญชี
        </button>
      </section>
    </div>
  )
}

export default function AccountsPage() {
  const { id, tab, sub } = useParams()
  const current = useApp((s) => s.instances.find((i) => i.id === id))
  if (!current) return <Home />
  if (tab === 'advanced') return <AdvancedPage instance={current} sub={sub} />
  return <AccountDetail instance={current} tab={tab} />
}
