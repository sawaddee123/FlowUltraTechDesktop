import { useState, type ReactNode } from 'react'
import { LogIn, RefreshCw, Trash2 } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import type { BotProxy, ProxyProtocol } from '@/lib/types'
import { useApp } from '@/lib/store'
import { Badge, Button, CopyButton, Switch } from '@/components/ui/base'
import { Field, Input, NumberInput, SecretInput, Select } from '@/components/ui/form'
import { Confirm } from '@/components/ui/overlay'
import { Section } from '@/components/ui/layout'
import type { TabProps } from './registry'
import { useTimers } from './tools/hooks'

function Info({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex items-center justify-between gap-4 py-2">
      <dt className="text-[13px] text-muted">{label}</dt>
      <dd className="flex min-w-0 items-center gap-1.5 text-sm text-fg">{children}</dd>
    </div>
  )
}

export default function SessionTab({ instance: i }: TabProps) {
  const nav = useNavigate()
  const later = useTimers()
  const proxies = useApp((s) => s.proxies)
  const patch = useApp((s) => s.patchInstance)
  const openLogin = useApp((s) => s.openLogin)
  const remove = useApp((s) => s.removeInstance)
  const toast = useApp((s) => s.toast)

  const [renewing, setRenewing] = useState(false)
  const [confirm, setConfirm] = useState(false)
  const [bp, setBp] = useState<BotProxy>(i.botProxy)
  const dirty = JSON.stringify(bp) !== JSON.stringify(i.botProxy)
  const setB = (p: Partial<BotProxy>) => setBp((c) => ({ ...c, ...p }))

  const renew = () => {
    setRenewing(true)
    later(() => {
      patch(i.id, { lease: { active: true, ttlMin: 60 } })
      setRenewing(false)
      toast({ tone: 'ok', title: 'ต่ออายุ Lease แล้ว', detail: 'เหลือ 60 นาที' })
    }, 700)
  }
  const saveBot = () => {
    patch(i.id, { botProxy: bp })
    toast({ tone: 'ok', title: 'บันทึก Proxy ของบอทแล้ว' })
  }

  return (
    <div className="flex h-full min-h-0 flex-col gap-6 overflow-y-auto px-6 py-4">
      <Section title="ข้อมูลเซสชัน">
        <dl className="max-w-xl divide-y divide-line/40">
          <Info label="โหมดโปรไฟล์"><Badge>{i.profileMode === 'direct' ? 'Direct' : 'Default'}</Badge></Info>
          <Info label="โฟลเดอร์"><span className="truncate font-mono text-[13px]">{i.folder}</span><CopyButton text={i.folder} label="คัดลอกโฟลเดอร์" /></Info>
          <Info label="CDP Port"><span className="font-mono text-[13px] tnum">{i.cdpPort}</span><CopyButton text={String(i.cdpPort)} label="คัดลอกพอร์ต" /></Info>
          <Info label="ช่อง">{i.channel ? <span className="font-mono text-[13px]">@{i.channel}</span> : <span className="text-subtle">ยังไม่ระบุ</span>}</Info>
          <Info label="Lease">
            <Badge tone={i.lease.active ? 'ok' : 'neutral'} dot>{i.lease.active ? `ใช้งาน · เหลือ ${i.lease.ttlMin} นาที` : 'ไม่ได้ใช้งาน'}</Badge>
            <Button size="sm" className="ml-1.5" loading={renewing} icon={<RefreshCw className="size-3.5" />} onClick={renew}>ต่ออายุ</Button>
          </Info>
          <Info label="สถานะล็อกอิน">
            <Badge tone={i.loggedIn ? 'ok' : 'warn'} dot>{i.loggedIn ? 'ล็อกอินแล้ว' : 'ยังไม่ล็อกอิน'}</Badge>
            <Button size="sm" className="ml-1.5" loading={i.status === 'starting'} disabled={i.status === 'running'}
              title={i.status === 'running' ? 'หยุดบัญชีก่อนจึงเปิดล็อกอินได้' : undefined}
              icon={<LogIn className="size-3.5" />} onClick={() => openLogin(i.id)}>เปิดล็อกอิน</Button>
          </Info>
        </dl>
      </Section>

      <Section title="Proxy ของบัญชี">
        <div className="max-w-xl">
          <Field label="Proxy ที่ผูก" hint="ทราฟฟิกของ Studio จะผ่าน Proxy นี้ผ่าน Proxifier">
            <Select value={i.proxyId ?? ''} onChange={(e) => patch(i.id, { proxyId: e.target.value || null })} className="h-9">
              <option value="">ไม่ใช้ Proxy (เชื่อมตรง)</option>
              {proxies.map((p) => (
                <option key={p.id} value={p.id} disabled={!p.enabled}>{p.name} · {p.host || 'ยังไม่ตั้งค่า'}:{p.port}{p.enabled ? '' : ' (ปิดอยู่)'}</option>
              ))}
            </Select>
          </Field>
        </div>
      </Section>

      <Section
        title="Proxy ของบอทตอบแชท"
        aside={<Switch checked={bp.enabled} onChange={(v) => setB({ enabled: v })} label="เปิดใช้ Proxy ของบอท" />}
      >
        <div className="grid max-w-xl grid-cols-2 gap-3">
          <Field label="โปรโตคอล">
            <Select value={bp.protocol} disabled={!bp.enabled} onChange={(e) => setB({ protocol: e.target.value as ProxyProtocol })} className="h-9">
              <option value="Socks5">Socks5</option><option value="Http">Http</option><option value="Https">Https</option>
            </Select>
          </Field>
          <Field label="พอร์ต"><NumberInput value={bp.port} min={1} max={65535} onChange={(n) => setB({ port: n })} /></Field>
          <Field label="โฮสต์" className="col-span-2"><Input mono value={bp.host} disabled={!bp.enabled} onChange={(e) => setB({ host: e.target.value })} placeholder="proxy.example.com" className="h-9" /></Field>
          <Field label="ชื่อผู้ใช้"><Input value={bp.username} disabled={!bp.enabled} onChange={(e) => setB({ username: e.target.value })} className="h-9" /></Field>
          <Field label="รหัสผ่าน"><SecretInput value={bp.password} disabled={!bp.enabled} onChange={(e) => setB({ password: e.target.value })} /></Field>
          <div className="col-span-2 flex justify-end gap-2">
            <Button variant="ghost" disabled={!dirty} onClick={() => setBp(i.botProxy)}>ยกเลิก</Button>
            <Button variant="primary" disabled={!dirty} onClick={saveBot}>บันทึก</Button>
          </div>
        </div>
      </Section>

      <Section title="โซนอันตราย">
        <div className="flex max-w-xl items-center justify-between gap-4 rounded-card bg-bad-soft px-4 py-3">
          <div className="min-w-0">
            <p className="text-sm font-medium text-fg">ลบบัญชีนี้</p>
            <p className="text-xs text-muted">ลบออกจากรายการ ไม่ลบโฟลเดอร์โปรไฟล์</p>
          </div>
          <Button variant="danger" icon={<Trash2 className="size-3.5" />} onClick={() => setConfirm(true)}>ลบบัญชี</Button>
        </div>
      </Section>

      <Confirm
        open={confirm} title={`ลบ ${i.name}?`} detail="การลบไม่สามารถย้อนกลับได้" confirmLabel="ลบบัญชี"
        onCancel={() => setConfirm(false)}
        onConfirm={() => { setConfirm(false); remove(i.id); nav('/accounts') }}
      />
    </div>
  )
}
