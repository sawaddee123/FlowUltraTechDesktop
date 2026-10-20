import { NavLink, useNavigate } from 'react-router-dom'
import { Globe, LogOut, MessagesSquare, ScanSearch, Settings2, Users, UserRound } from 'lucide-react'
import { useApp, selectRunning } from '@/lib/store'
import { cn } from '@/lib/cn'
import { Menu } from '@/components/ui/overlay'
import { Brand } from './Brand'

export function Sidebar() {
  const nav = useNavigate()
  const running = useApp(selectRunning)
  const total = useApp((s) => s.instances.length)
  const alerts = useApp((s) => s.instances.filter((i) => i.captcha).length)
  const proxies = useApp((s) => s.proxies.filter((p) => p.enabled).length)
  const rules = useApp((s) => s.rules.filter((r) => r.enabled).length)
  const license = useApp((s) => s.license)
  const logout = useApp((s) => s.logout)

  const items = [
    { to: '/accounts', label: 'บัญชีสตูดิโอ', icon: Users, meta: `${running}/${total}` },
    { to: '/captcha', label: 'Captcha', icon: ScanSearch, alert: alerts },
    { to: '/proxy', label: 'Proxy', icon: Globe, meta: String(proxies) },
    { to: '/rules', label: 'กฎตอบกลับ', icon: MessagesSquare, meta: String(rules) },
  ]

  return (
    <aside className="flex w-[216px] shrink-0 flex-col bg-surface-2/50 px-3 pb-3 pt-4 max-[1100px]:w-[68px]">
      <div className="px-2 pb-5 max-[1100px]:hidden"><Brand /></div>
      <div className="hidden px-2 pb-5 max-[1100px]:block"><Brand compact /></div>

      <nav aria-label="เมนูหลัก" className="flex flex-col gap-0.5">
        {items.map((it) => (
          <NavLink
            key={it.to} to={it.to} title={it.label}
            className={({ isActive }) => cn(
              'group relative flex h-10 items-center gap-3 rounded-ctl px-2.5 text-sm font-medium transition-colors max-[1100px]:justify-center',
              isActive ? 'bg-surface text-fg shadow-sm' : 'text-muted hover:bg-surface/60 hover:text-fg',
            )}
          >
            {({ isActive }) => (
              <>
                <span className={cn('absolute left-0 h-4 w-0.5 rounded-full bg-primary transition-opacity', isActive ? 'opacity-100' : 'opacity-0')} />
                <it.icon className={cn('size-[18px] shrink-0', isActive ? 'text-primary' : 'text-subtle group-hover:text-muted')} aria-hidden />
                <span className="flex-1 max-[1100px]:hidden">{it.label}</span>
                {it.alert ? (
                  <span className="grid h-5 min-w-5 place-items-center rounded-full bg-bad px-1.5 text-[11px] font-semibold text-on-primary max-[1100px]:absolute max-[1100px]:-right-0.5 max-[1100px]:-top-0.5">{it.alert}</span>
                ) : it.meta ? (
                  <span className="text-xs text-subtle tnum max-[1100px]:hidden">{it.meta}</span>
                ) : null}
              </>
            )}
          </NavLink>
        ))}
      </nav>

      <div className="mt-auto flex flex-col gap-0.5">
        <NavLink
          to="/settings" title="ตั้งค่า"
          className={({ isActive }) => cn(
            'flex h-10 items-center gap-3 rounded-ctl px-2.5 text-sm font-medium transition-colors max-[1100px]:justify-center',
            isActive ? 'bg-surface text-fg shadow-sm' : 'text-muted hover:bg-surface/60 hover:text-fg',
          )}
        >
          <Settings2 className="size-[18px] text-subtle" aria-hidden />
          <span className="max-[1100px]:hidden">ตั้งค่า</span>
        </NavLink>

        <Menu
          align="left" width={232}
          items={[
            { label: 'บัญชีและสิทธิ์', icon: <UserRound />, onSelect: () => nav('/settings/account') },
            { label: 'ออกจากระบบ', icon: <LogOut />, danger: true, divider: true, onSelect: () => { logout(); nav('/login') } },
          ]}
          trigger={({ toggle }) => (
            <button type="button" onClick={toggle} className="mt-1 flex w-full items-center gap-2.5 rounded-ctl p-2 text-left transition-colors hover:bg-surface/60 max-[1100px]:justify-center">
              <span className="grid size-8 shrink-0 place-items-center rounded-full bg-primary-soft text-sm font-semibold text-primary">{license.username[0].toUpperCase()}</span>
              <span className="min-w-0 flex-1 max-[1100px]:hidden">
                <span className="block truncate text-sm font-medium leading-tight">{license.username}</span>
                <span className="block truncate text-xs text-subtle">แผน {license.plan} · ถึง {license.expiresAt}</span>
              </span>
            </button>
          )}
        />
      </div>
    </aside>
  )
}
