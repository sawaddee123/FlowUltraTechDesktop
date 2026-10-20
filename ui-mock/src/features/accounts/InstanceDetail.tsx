import { Suspense, useState } from 'react'
import { Ban, Copy, Eye, EyeOff, FolderSync, LogIn, MonitorUp, MoreHorizontal, Play, RotateCw, ScanSearch, Square, Trash2, CircleAlert } from 'lucide-react'
import type { Instance } from '@/lib/types'
import { useShallow } from 'zustand/react/shallow'
import { useApp, selectRunning } from '@/lib/store'
import { captchaLabel, displayName, fmtNum, statusMeta, uptime, useNow } from '@/lib/format'
import { Avatar, Badge, Button, IconButton, Skeleton, Stat } from '@/components/ui/base'
import { Confirm, Menu } from '@/components/ui/overlay'
import { Tabs } from '@/components/ui/layout'
import { isTabId, mainTabs, tabComponents, toolTabs, type TabId } from './tabs/registry'
import { Wrench } from 'lucide-react'

function TabFallback() {
  return (
    <div className="flex flex-col gap-4 p-6" aria-busy="true" aria-label="กำลังโหลด">
      <Skeleton className="h-8 w-1/3" />
      <Skeleton className="h-24 w-full" />
      <Skeleton className="h-40 w-full" />
    </div>
  )
}

export function InstanceDetail({ instance: i, tab, onTab }: { instance: Instance; tab?: string; onTab: (t: TabId) => void }) {
  const now = useNow(30_000)
  const { start, stop, setHidden, openLogin, remove, solve, dismiss } = useApp(useShallow((s) => ({
    start: s.startInstance, stop: s.stopInstance, setHidden: s.setWindowHidden, openLogin: s.openLogin,
    remove: s.removeInstance, solve: s.solveCaptcha, dismiss: s.dismissCaptcha,
  })))
  const quotaFull = useApp((s) => selectRunning(s) >= s.license.maxAccounts)
  const proxyName = useApp((s) => s.proxies.find((p) => p.id === i.proxyId)?.name)
  const toast = useApp((s) => s.toast)
  const [confirmDel, setConfirmDel] = useState(false)

  const active: TabId = isTabId(tab) ? tab : 'live'
  const Body = tabComponents[active]
  const meta = statusMeta[i.status]
  const busy = i.status === 'starting'
  const running = i.status === 'running'
  const isTool = toolTabs.some((t) => t.id === active)
  const proxyLabel = i.botProxy.enabled ? `Proxy ${i.botProxy.protocol} (เฉพาะบอท)` : proxyName ? `Proxy ${proxyName}` : 'เชื่อมต่อตรง'

  return (
    <section aria-label={`รายละเอียด ${displayName(i)}`} className="flex h-full min-w-0 flex-1 flex-col rounded-card bg-surface">
      {/* ── ส่วนหัว: ตัวตน + คำสั่งหลัก (ตำแหน่งปุ่มคงที่ทุกสถานะ) ── */}
      <header className="flex flex-wrap items-center gap-x-4 gap-y-3 px-6 pb-4 pt-5">
        <Avatar name={i.loggedIn ? displayName(i) : '?'} hue={i.hue} size={48} />
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="truncate text-lg font-semibold leading-tight">{i.loggedIn ? displayName(i) : i.name}</h2>
            <Badge tone={meta.tone} dot>{meta.label}</Badge>
            {running && <Badge tone="info">{i.windowHidden ? 'ซ่อนหน้าต่าง' : 'แสดงหน้าต่าง'}</Badge>}
          </div>
          <p className="mt-0.5 truncate text-sm text-subtle">
            {i.loggedIn ? `@${i.handle}` : 'ยังไม่เข้าสู่ระบบ'} · CDP {i.cdpPort} · {proxyLabel}
            {running && <> · ไลฟ์มา {uptime(i.startedAt, now)}</>}
          </p>
        </div>

        <div className="flex items-center gap-1.5">
          {running && (
            <IconButton label={i.windowHidden ? 'แสดงหน้าต่าง' : 'ซ่อนหน้าต่าง'} aria-pressed={i.windowHidden} onClick={() => setHidden(i.id, !i.windowHidden)}>
              {i.windowHidden ? <EyeOff className="size-[18px]" /> : <Eye className="size-[18px]" />}
            </IconButton>
          )}
          {!i.loggedIn ? (
            <Button variant="primary" className="min-w-32" loading={busy} icon={<LogIn className="size-4" />} onClick={() => openLogin(i.id)}>เข้าสู่ระบบ</Button>
          ) : running ? (
            <Button className="min-w-24" icon={<Square className="size-3.5 fill-current" />} onClick={() => stop(i.id)}>หยุด</Button>
          ) : (
            <Button variant="primary" className="min-w-32" loading={busy} disabled={quotaFull} title={quotaFull ? 'เปิดครบโควต้าสูงสุดแล้ว' : undefined}
              icon={<Play className="size-3.5 fill-current" />} onClick={() => start(i.id)}>
              {busy ? 'กำลังเริ่ม' : 'เริ่มทำงาน'}
            </Button>
          )}
          <Menu
            items={[
              { label: 'เปิด Studio (เพื่อล็อกอินใหม่)', icon: <MonitorUp />, disabled: running, onSelect: () => openLogin(i.id) },
              { label: 'รีเฟรชโปรไฟล์', icon: <FolderSync />, onSelect: () => toast({ tone: 'ok', title: 'รีเฟรชโปรไฟล์แล้ว', detail: 'อ่าน services.json' }) },
              { label: 'ลบบัญชี', icon: <Trash2 />, danger: true, divider: true, onSelect: () => setConfirmDel(true) },
            ]}
            trigger={({ toggle }) => <IconButton label="เพิ่มเติม" onClick={toggle}><MoreHorizontal className="size-[18px]" /></IconButton>}
          />
        </div>
      </header>

      {/* ── การแจ้งเตือนที่ต้องลงมือ: โผล่เฉพาะเมื่อมี และไม่ดันปุ่มด้านบน ── */}
      {i.status === 'error' && i.error && (
        <div role="alert" className="mx-6 mb-3 flex animate-rise items-center gap-3 rounded-ctl bg-bad-soft px-3.5 py-2.5 text-sm text-bad">
          <CircleAlert className="size-4 shrink-0" aria-hidden />
          <span className="min-w-0 flex-1 truncate" title={i.error}>{i.error}</span>
          <Button size="sm" variant="ghost" className="text-bad hover:text-bad" icon={<Copy className="size-3.5" />} onClick={() => { void navigator.clipboard?.writeText(i.error ?? ''); toast({ tone: 'ok', title: 'คัดลอก Error แล้ว' }) }}>คัดลอก</Button>
          <Button size="sm" variant="danger" icon={<RotateCw className="size-3.5" />} onClick={() => start(i.id)}>ลองใหม่</Button>
        </div>
      )}
      {i.captcha && (
        <div role="alert" className="mx-6 mb-3 flex animate-rise items-center gap-3 rounded-ctl bg-bad-soft px-3.5 py-2.5 text-sm text-bad">
          <ScanSearch className="size-4 shrink-0" aria-hidden />
          <span className="min-w-0 flex-1 font-medium">พบ {captchaLabel[i.captcha.kind]} Captcha <span className="font-normal tnum">· เหลือ {i.captcha.countdown} วินาที</span></span>
          <Button size="sm" variant="primary" onClick={() => solve(i.id, 'ai-manual')}>แก้ทันที</Button>
          {active !== 'captcha' && <Button size="sm" variant="ghost" className="text-bad hover:text-bad" onClick={() => onTab('captcha')}>ดูรายละเอียด</Button>}
          <IconButton label="ปิด" size="sm" onClick={() => dismiss(i.id)} className="text-bad"><Ban className="size-4" /></IconButton>
        </div>
      )}

      {/* ── ตัวเลขสด ── */}
      {running && (
        <div className="grid grid-cols-3 gap-x-6 gap-y-3 px-6 pb-4 sm:grid-cols-6">
          <Stat label="ผู้ชม" value={fmtNum(i.stats.viewers)} />
          <Stat label="แชท" value={fmtNum(i.stats.chats)} />
          <Stat label="เพชร" value={fmtNum(i.stats.diamonds)} sub={`${i.stats.gifts} ของขวัญ`} />
          <Stat label="ไลก์" value={fmtNum(i.stats.likes)} />
          <Stat label="ผู้ติดตามใหม่" value={fmtNum(i.stats.follows)} />
          <Stat label="บอทตอบ" value={fmtNum(i.stats.botReplies)} />
        </div>
      )}

      <Tabs
        label="หมวดรายละเอียด" value={active} onChange={onTab} className="px-4 shadow-[inset_0_-1px_0_var(--line)]"
        tabs={[
          ...mainTabs.map((t) => ({ ...t, badge: t.id === 'captcha' && i.captcha ? <span className="size-1.5 rounded-full bg-bad" aria-label="มี Captcha" /> : undefined })),
          ...(isTool ? toolTabs.filter((t) => t.id === active) : []),
        ]}
        trailing={
          <Menu
            width={210}
            items={toolTabs.map((t) => ({ label: t.label, icon: <Wrench />, onSelect: () => onTab(t.id) }))}
            trigger={({ toggle }) => <Button size="sm" variant="ghost" className="mb-1" icon={<Wrench className="size-4" />} onClick={toggle}>เครื่องมือ</Button>}
          />
        }
      />

      <div className="min-h-0 flex-1 overflow-hidden">
        <Suspense fallback={<TabFallback />}><Body key={`${i.id}-${active}`} instance={i} /></Suspense>
      </div>

      <Confirm
        open={confirmDel} title={`ลบ ${displayName(i)}?`} detail="หยุดการทำงานแล้วลบบัญชีนี้ออกจากรายการ ย้อนกลับไม่ได้" confirmLabel="ลบบัญชี"
        onCancel={() => setConfirmDel(false)} onConfirm={() => { setConfirmDel(false); stop(i.id); remove(i.id) }}
      />
    </section>
  )
}
