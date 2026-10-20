import { CircleAlert, LogIn, RotateCw, ScanSearch, Sparkles } from 'lucide-react'
import { useApp } from '@/lib/store'
import { displayName, fmtNum, statusMeta, uptime, useNow, captchaLabel } from '@/lib/format'
import { Avatar, Badge, Button, Stat } from '@/components/ui/base'
import { DataTable, Section, tdc } from '@/components/ui/layout'

export function FleetSummary({ onSelect }: { onSelect: (id: string, tab?: string) => void }) {
  const instances = useApp((s) => s.instances)
  const proxies = useApp((s) => s.proxies)
  const solve = useApp((s) => s.solveCaptcha)
  const start = useApp((s) => s.startInstance)
  const openLogin = useApp((s) => s.openLogin)
  const now = useNow(30_000)

  const live = instances.filter((i) => i.status === 'running')
  const sum = (f: (i: (typeof instances)[number]) => number) => live.reduce((a, i) => a + f(i), 0)
  const todo = instances.filter((i) => i.captcha || i.status === 'error' || !i.loggedIn)

  return (
    <div className="scroll-y h-full px-6 pb-6 animate-fade-in">
      <div className="grid grid-cols-2 gap-x-8 gap-y-5 pb-5 pt-6 sm:grid-cols-3 lg:grid-cols-5">
        <Stat label="ผู้ชมรวม" value={fmtNum(sum((i) => i.stats.viewers))} sub={`${live.length} บัญชีไลฟ์อยู่`} />
        <Stat label="เพชรรวม" value={fmtNum(sum((i) => i.stats.diamonds))} sub={`${sum((i) => i.stats.gifts)} ของขวัญ`} />
        <Stat label="แชท" value={fmtNum(sum((i) => i.stats.chats))} />
        <Stat label="บอทตอบ" value={fmtNum(sum((i) => i.stats.botReplies))} />
        <Stat label="Captcha รอแก้" value={instances.filter((i) => i.captcha).length} tone={instances.some((i) => i.captcha) ? 'bad' : undefined} />
      </div>

      <Section title="ต้องดำเนินการ" aside={todo.length > 0 && <Badge tone="warn">{todo.length}</Badge>}>
        {todo.length === 0 ? (
          <p className="flex items-center gap-2 text-sm text-ok"><Sparkles className="size-4" aria-hidden />ทุกบัญชีปกติ</p>
        ) : (
          <ul className="flex flex-col gap-1">
            {todo.map((i) => (
              <li key={i.id} className="flex items-center gap-3 rounded-ctl px-2 py-2 hover:bg-surface-2/60">
                <Avatar name={displayName(i)} hue={i.hue} size={32} />
                <button type="button" onClick={() => onSelect(i.id)} className="min-w-0 flex-1 text-left">
                  <span className="block truncate text-sm font-medium">{displayName(i)}</span>
                  <span className="flex items-center gap-1.5 truncate text-xs text-subtle">
                    {i.captcha ? <><ScanSearch className="size-3 text-bad" aria-hidden />{captchaLabel[i.captcha.kind]} Captcha · เหลือ {i.captcha.countdown} วินาที</>
                      : i.status === 'error' ? <><CircleAlert className="size-3 text-bad" aria-hidden />{i.error}</>
                      : 'ยังไม่เข้าสู่ระบบ TikTok'}
                  </span>
                </button>
                {i.captcha ? <Button size="sm" variant="primary" onClick={() => solve(i.id, 'ai-manual')}>แก้ทันที</Button>
                  : i.status === 'error' ? <Button size="sm" icon={<RotateCw className="size-3.5" />} onClick={() => start(i.id)}>ลองใหม่</Button>
                  : <Button size="sm" icon={<LogIn className="size-3.5" />} onClick={() => openLogin(i.id)}>เข้าสู่ระบบ</Button>}
              </li>
            ))}
          </ul>
        )}
      </Section>

      <Section title="รายบัญชี">
        <div className="overflow-x-auto">
          <DataTable head={[{ label: 'บัญชี' }, { label: 'สถานะ' }, { label: 'ผู้ชม', className: 'text-right' }, { label: 'เพชร', className: 'text-right' }, { label: 'แชท', className: 'text-right' }, { label: 'ไลฟ์มา' }, { label: 'Proxy' }]}>
            {instances.map((i) => {
              const m = statusMeta[i.status]
              const px = i.botProxy.enabled ? 'เฉพาะบอท' : proxies.find((p) => p.id === i.proxyId)?.name ?? 'ตรง'
              return (
                <tr key={i.id} onClick={() => onSelect(i.id)} className="cursor-pointer transition-colors hover:bg-surface-2/60">
                  <td className={tdc}><span className="font-medium">{displayName(i)}</span></td>
                  <td className={tdc}><Badge tone={m.tone} dot>{m.label}</Badge></td>
                  <td className={`${tdc} text-right`}>{i.status === 'running' ? fmtNum(i.stats.viewers) : '—'}</td>
                  <td className={`${tdc} text-right`}>{i.status === 'running' ? fmtNum(i.stats.diamonds) : '—'}</td>
                  <td className={`${tdc} text-right`}>{i.status === 'running' ? fmtNum(i.stats.chats) : '—'}</td>
                  <td className={`${tdc} text-muted`}>{uptime(i.startedAt, now)}</td>
                  <td className={`${tdc} text-muted`}>{px}</td>
                </tr>
              )
            })}
          </DataTable>
        </div>
      </Section>
    </div>
  )
}
