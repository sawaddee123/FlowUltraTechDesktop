import { memo } from 'react'
import { Link } from 'react-router-dom'
import { ChevronRight, Clock, Eye, Gem, LogIn, Play, Square } from 'lucide-react'
import type { Instance } from '@/lib/types'
import { useApp, selectRunning } from '@/lib/store'
import { cardStatus, displayName, fmtNum, uptime, useNow } from '@/lib/format'
import { Avatar, Badge, Button } from '@/components/ui/base'

export const AccountCard = memo(function AccountCard({ instance: i }: { instance: Instance }) {
  const now = useNow(30_000)
  const autoSolve = useApp((s) => s.settings.autoSolve)
  const quotaFull = useApp((s) => selectRunning(s) >= s.license.maxAccounts)
  const start = useApp((s) => s.startInstance)
  const stop = useApp((s) => s.stopInstance)
  const openLogin = useApp((s) => s.openLogin)
  const st = cardStatus(i, autoSolve)
  const running = i.status === 'running'
  const busy = i.status === 'starting'
  const name = i.loggedIn ? displayName(i) : i.name

  return (
    <article className="flex flex-col gap-4.5 rounded-card bg-surface p-5">
      <div className="flex items-center gap-3">
        <Avatar name={i.loggedIn ? name : '?'} hue={i.hue} size={44} />
        <div className="min-w-0 flex-1">
          <Link to={`/accounts/${i.id}`} className="block truncate text-base font-semibold text-fg hover:text-primary">{name}</Link>
          <p className="truncate text-[13px] text-muted">{i.loggedIn ? `@${i.handle}` : 'ยังไม่ได้เชื่อมบัญชี TikTok'}</p>
        </div>
        <Badge tone={st.tone} dot>{st.label}</Badge>
      </div>

      <div className="flex gap-6 text-sm text-muted">
        <span className="inline-flex items-center gap-1.5"><Eye className="size-4" aria-hidden /><b className="font-medium text-fg tnum">{running ? fmtNum(i.stats.viewers) : '—'}</b> ผู้ชม</span>
        <span className="inline-flex items-center gap-1.5"><Gem className="size-4" aria-hidden /><b className="font-medium text-fg tnum">{running ? fmtNum(i.stats.diamonds) : '—'}</b> เพชร</span>
      </div>
      <p className="flex items-center gap-2 text-[13px] text-muted">
        <Clock className="size-4" aria-hidden />{running ? `ไลฟ์มา ${uptime(i.startedAt, now)}` : busy ? 'กำลังเปิด Studio…' : 'ยังไม่เริ่ม'}
      </p>

      <div className="flex gap-2">
        {!i.loggedIn ? (
          <Button variant="primary" className="h-11 flex-1 text-[15px]" loading={busy} icon={<LogIn className="size-4" />} onClick={() => openLogin(i.id)}>ล็อกอิน TikTok</Button>
        ) : running ? (
          <Button className="h-11 flex-1 text-[15px]" icon={<Square className="size-3.5 fill-current" />} onClick={() => stop(i.id)}>หยุดไลฟ์</Button>
        ) : (
          <Button variant="primary" className="h-11 flex-1 text-[15px]" loading={busy} disabled={quotaFull}
            title={quotaFull ? 'เปิดครบจำนวนตามแพ็กเกจแล้ว' : undefined} icon={<Play className="size-3.5 fill-current" />} onClick={() => start(i.id)}>
            {busy ? 'กำลังเริ่ม' : 'เริ่มไลฟ์'}
          </Button>
        )}
        <Link to={`/accounts/${i.id}`} aria-label={`ดูรายละเอียด ${name}`} title="ดูรายละเอียด"
          className="grid size-11 shrink-0 place-items-center rounded-ctl bg-surface-2 text-fg transition-colors hover:bg-surface-3">
          <ChevronRight className="size-[18px]" aria-hidden />
        </Link>
      </div>
    </article>
  )
})
