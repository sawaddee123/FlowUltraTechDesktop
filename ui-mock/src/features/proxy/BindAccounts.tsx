import { memo, useState } from 'react'
import { ArrowRightLeft, Globe, RotateCw } from 'lucide-react'
import { useShallow } from 'zustand/react/shallow'
import type { ProxyItem } from '@/lib/types'
import { useApp } from '@/lib/store'
import { displayName, statusMeta } from '@/lib/format'
import { Avatar, Button, StatusDot, Switch } from '@/components/ui/base'
import { Section } from '@/components/ui/layout'

/** แถวบัญชี 1 แถว — subscribe เฉพาะค่าที่ใช้ เพื่อไม่ re-render ทุกครั้งที่สถิติสดขยับ */
const BindRow = memo(function BindRow({ id, proxy, otherName }: { id: string; proxy: ProxyItem; otherName: (pid: string) => string }) {
  const patch = useApp((s) => s.patchInstance)
  const toast = useApp((s) => s.toast)
  const i = useApp(useShallow((s) => {
    const x = s.instances.find((v) => v.id === id)
    if (!x) return { name: '', handle: '', hue: 0, loggedIn: false, status: 'stopped' as const, proxyId: null as string | null, custom: false }
    return { name: displayName(x), handle: x.loggedIn ? '@' + x.handle : 'ยังไม่เข้าสู่ระบบ', hue: x.hue, loggedIn: x.loggedIn, status: x.status, proxyId: x.proxyId, custom: x.botProxy.enabled }
  }))
  const [asking, setAsking] = useState(false)
  const mine = i.proxyId === proxy.id
  const other = i.proxyId && !mine ? otherName(i.proxyId) : null
  const meta = statusMeta[i.status]

  const change = (on: boolean) => {
    if (!on) { patch(id, { proxyId: null }); return }
    if (other) { setAsking(true); return }
    patch(id, { proxyId: proxy.id })
  }
  const move = () => {
    patch(id, { proxyId: proxy.id }); setAsking(false)
    toast({ tone: 'ok', title: `ย้าย ${i.name} มาใช้ ${proxy.name}`, detail: `จาก ${other}` })
  }

  return (
    <li className="rounded-ctl transition-colors hover:bg-surface-2/50">
      <div className="flex items-center gap-3 px-2 py-2">
        <Avatar name={i.loggedIn ? i.name : '?'} hue={i.hue} size={30} className={i.loggedIn ? undefined : 'opacity-50'} />
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-medium">{i.name}</p>
          <p className="flex items-center gap-1.5 truncate text-xs text-subtle">
            <StatusDot tone={meta.tone} />{meta.label} · {i.handle}
            {other && <span className="inline-flex items-center gap-1 text-warn"><Globe className="size-3" aria-hidden />ใช้ {other}</span>}
            {i.custom && <span className="text-muted">· Proxy เฉพาะบอทมีผลก่อน</span>}
          </p>
        </div>
        {mine && i.status === 'running' && (
          <span className="hidden items-center gap-1 text-xs text-warn sm:inline-flex"><RotateCw className="size-3" aria-hidden />เริ่มใหม่เพื่อใช้</span>
        )}
        <Switch checked={mine} onChange={change} label={`ใช้ ${proxy.name} กับ ${i.name}`} />
      </div>
      {asking && other && (
        <div role="alert" className="mx-2 mb-2 flex animate-rise flex-wrap items-center gap-2 rounded-ctl bg-warn-soft px-3 py-2 text-sm text-warn">
          <ArrowRightLeft className="size-4 shrink-0" aria-hidden />
          <span className="min-w-0 flex-1">ย้ายจาก {other} มาใช้ {proxy.name}?</span>
          <Button size="sm" variant="secondary" onClick={() => setAsking(false)}>ยกเลิก</Button>
          <Button size="sm" variant="secondary" onClick={move}>ย้าย</Button>
        </div>
      )}
    </li>
  )
})

export function BindAccounts({ proxy }: { proxy: ProxyItem }) {
  const ids = useApp(useShallow((s) => s.instances.map((i) => i.id)))
  const used = useApp((s) => s.instances.filter((i) => i.proxyId === proxy.id).length)
  const names = useApp(useShallow((s) => s.proxies.map((p) => `${p.id}=${p.name}`)))
  const otherName = (pid: string) => names.find((n) => n.startsWith(pid + '='))?.slice(pid.length + 1) ?? 'Proxy อื่น'

  return (
    <Section title="ใช้กับบัญชี" aside={<span className="text-xs text-subtle tnum">{used}/{ids.length} บัญชี</span>}>
      <ul className="-mx-2 flex flex-col gap-0.5">
        {ids.map((id) => <BindRow key={id} id={id} proxy={proxy} otherName={otherName} />)}
      </ul>
    </Section>
  )
}
