import { Suspense, type ReactNode } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ChevronLeft, CircleAlert, LogIn, MoreHorizontal, Play, RotateCw, Square, TriangleAlert } from 'lucide-react'
import { useShallow } from 'zustand/react/shallow'
import type { Instance } from '@/lib/types'
import { useApp, selectRunning } from '@/lib/store'
import { cardStatus, displayName, needsHelp, uptime, useNow } from '@/lib/format'
import { Avatar, Badge, Button, Skeleton, Spinner } from '@/components/ui/base'
import { Tabs } from '@/components/ui/layout'
import { isTabId, mainTabs, tabComponents, type TabId } from './tabs/registry'

export function TabFallback() {
  return (
    <div className="flex flex-col gap-4 py-6" aria-busy="true" aria-label="กำลังโหลด">
      <Skeleton className="h-24 w-full" />
      <Skeleton className="h-64 w-full" />
    </div>
  )
}

/** หัวหน้าบัญชีที่ใช้ร่วมกับหน้าขั้นสูง — ลิงก์กลับ + ตัวตน + ปุ่มหลักอยู่ที่เดิมทุกสถานะ */
export function AccountHeader({ instance: i, back, actions }: { instance: Instance; back: { to: string; label: string }; actions?: ReactNode }) {
  const now = useNow(30_000)
  const autoSolve = useApp((s) => s.settings.autoSolve)
  const st = cardStatus(i, autoSolve)
  return (
    <>
      <Link to={back.to} className="inline-flex h-9 items-center gap-1.5 self-start text-sm text-muted hover:text-fg">
        <ChevronLeft className="size-4" aria-hidden />{back.label}
      </Link>
      <header className="flex flex-wrap items-center gap-4">
        <Avatar name={i.loggedIn ? displayName(i) : '?'} hue={i.hue} size={56} />
        <div className="min-w-0 flex-[1_1_240px]">
          <h1 className="truncate text-2xl font-semibold tracking-tight">{i.loggedIn ? displayName(i) : i.name}</h1>
          <p className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-muted">
            <Badge tone={st.tone} dot>{st.label}</Badge>
            {i.status === 'running' && <span>ไลฟ์มา {uptime(i.startedAt, now)}</span>}
            {i.loggedIn && <span>@{i.handle}</span>}
          </p>
        </div>
        {actions}
      </header>
    </>
  )
}

export function AccountDetail({ instance: i, tab }: { instance: Instance; tab?: string }) {
  const nav = useNavigate()
  const { start, stop, setHidden, openLogin, setHelpId, autoSolve } = useApp(useShallow((s) => ({
    start: s.startInstance, stop: s.stopInstance, setHidden: s.setWindowHidden, openLogin: s.openLogin,
    setHelpId: s.setHelpId, autoSolve: s.settings.autoSolve,
  })))
  const quotaFull = useApp((s) => selectRunning(s) >= s.license.maxAccounts)
  const active: TabId = isTabId(tab) ? tab : 'live'
  const Body = tabComponents[active]
  const running = i.status === 'running'
  const busy = i.status === 'starting'

  return (
    <div className="flex min-h-full flex-col gap-5 px-10 pb-10 pt-6 max-[900px]:px-5">
      <AccountHeader
        instance={i} back={{ to: '/accounts', label: 'บัญชีทั้งหมด' }}
        actions={
          <div className="flex flex-wrap items-center gap-2">
            {running && (
              <Button className="h-11 px-4.5 text-[15px]" aria-pressed={!i.windowHidden} onClick={() => setHidden(i.id, !i.windowHidden)}>
                {i.windowHidden ? 'แสดงหน้าต่าง Studio' : 'ซ่อนหน้าต่าง Studio'}
              </Button>
            )}
            {!i.loggedIn ? (
              <Button variant="primary" className="h-11 min-w-36 px-5 text-[15px]" loading={busy} icon={<LogIn className="size-4" />} onClick={() => openLogin(i.id)}>ล็อกอิน TikTok</Button>
            ) : running ? (
              <Button className="h-11 min-w-32 px-5 text-[15px]" icon={<Square className="size-3.5 fill-current" />} onClick={() => stop(i.id)}>หยุดไลฟ์</Button>
            ) : (
              <Button variant="primary" className="h-11 min-w-32 px-5 text-[15px]" loading={busy} disabled={quotaFull}
                title={quotaFull ? 'เปิดครบจำนวนตามแพ็กเกจแล้ว' : undefined} icon={<Play className="size-3.5 fill-current" />} onClick={() => start(i.id)}>
                {busy ? 'กำลังเริ่ม' : 'เริ่มไลฟ์'}
              </Button>
            )}
            <Link to={`/accounts/${i.id}/advanced`} aria-label="ขั้นสูง" title="ขั้นสูง"
              className="grid size-11 place-items-center rounded-ctl bg-surface-2 text-fg transition-colors hover:bg-surface-3">
              <MoreHorizontal className="size-[18px]" aria-hidden />
            </Link>
          </div>
        }
      />

      {i.status === 'error' && i.error && (
        <div role="alert" className="flex animate-rise flex-wrap items-center gap-3 rounded-card bg-bad-soft px-4 py-3 text-sm">
          <CircleAlert className="size-5 shrink-0 text-bad" aria-hidden />
          <span className="min-w-0 flex-[1_1_240px]">{i.error}</span>
          <Button variant="danger" icon={<RotateCw className="size-4" />} onClick={() => start(i.id)}>ลองใหม่</Button>
        </div>
      )}
      {i.captcha && (needsHelp(i, autoSolve) ? (
        <div role="status" className="flex animate-rise flex-wrap items-center gap-3 rounded-card bg-warn-soft px-4 py-3 text-sm">
          <TriangleAlert className="size-5 shrink-0 text-warn" aria-hidden />
          <span className="min-w-0 flex-[1_1_240px] font-medium">ต้องให้คุณช่วยแก้ Captcha <span className="font-normal text-muted tnum">· เหลือ {i.captcha.countdown} วินาที</span></span>
          <Button className="bg-warn text-on-primary hover:bg-warn hover:opacity-90" onClick={() => setHelpId(i.id)}>แก้ตอนนี้</Button>
        </div>
      ) : (
        <div role="status" className="flex animate-rise items-center gap-3 rounded-card bg-primary-soft px-4 py-3 text-sm">
          <Spinner className="text-primary" />AI กำลังแก้ Captcha ให้…
        </div>
      ))}

      <Tabs
        label="หมวด" value={active} onChange={(t) => nav(`/accounts/${i.id}/${t}`)} className="shadow-[inset_0_-1px_0_var(--line)]"
        tabs={mainTabs.map((t) => ({ ...t, badge: t.disabled ? <Badge>เร็วๆ นี้</Badge> : undefined }))}
      />

      <Suspense fallback={<TabFallback />}><Body key={`${i.id}-${active}`} instance={i} /></Suspense>
    </div>
  )
}
