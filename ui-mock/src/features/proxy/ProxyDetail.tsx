import { useState } from 'react'
import { CircleAlert, Gauge, Trash2, Zap } from 'lucide-react'
import type { ProxyItem, ProxyProtocol } from '@/lib/types'
import { useApp } from '@/lib/store'
import { ago } from '@/lib/format'
import { Badge, Button, IconButton, Skeleton, Stat } from '@/components/ui/base'
import { Field, Input, NumberInput, Select, SecretInput } from '@/components/ui/form'
import { Confirm } from '@/components/ui/overlay'
import { BindAccounts } from './BindAccounts'
import { TestPill, protocols } from './proxyUi'

/** ผลทดสอบ: จองพื้นที่คงที่ ไม่ให้เลย์เอาต์กระโดดระหว่างทดสอบ/ได้ผล */
function TestResult({ p }: { p: ProxyItem }) {
  const t = p.test
  return (
    <div className="min-h-[68px]" aria-live="polite">
      {t === 'testing' ? (
        <div className="grid grid-cols-3 gap-6" aria-busy="true" aria-label="กำลังทดสอบ">
          {[0, 1, 2].map((k) => <div key={k}><Skeleton className="h-3 w-14" /><Skeleton className="mt-2 h-6 w-24" /></div>)}
        </div>
      ) : !t ? (
        <p className="flex h-[68px] items-center gap-2 text-sm text-subtle"><Gauge className="size-4" aria-hidden />ยังไม่ทดสอบ · กด ทดสอบ IP/Ping เพื่อดู IP ขาออกและความหน่วง</p>
      ) : t.ok ? (
        <div className="grid grid-cols-3 gap-6">
          <Stat label="IP ขาออก" value={<span className="font-mono text-lg">{t.ip}</span>} />
          <Stat label="Ping" value={`${t.pingMs} ms`} tone={t.pingMs > 150 ? 'warn' : 'ok'} />
          <Stat label="ทดสอบเมื่อ" value={<span className="text-base">{ago(t.at)}</span>} />
        </div>
      ) : (
        <div role="alert" className="flex items-center gap-3 rounded-ctl bg-bad-soft px-3.5 py-3 text-sm text-bad">
          <CircleAlert className="size-4 shrink-0" aria-hidden />
          <span className="min-w-0 flex-1">เชื่อมต่อไม่ได้ · ตรวจ host, port และรหัสผ่าน <span className="text-bad/80">({ago(t.at)})</span></span>
        </div>
      )}
    </div>
  )
}

export function ProxyDetail({ proxy: p, usedBy }: { proxy: ProxyItem; usedBy: number }) {
  const patch = useApp((s) => s.patchProxy)
  const test = useApp((s) => s.testProxy)
  const remove = useApp((s) => s.removeProxy)
  const toast = useApp((s) => s.toast)
  const [confirm, setConfirm] = useState(false)
  const [touched, setTouched] = useState(false)
  const touch = () => setTouched(true)
  const testing = p.test === 'testing'
  const hostErr = touched && !p.host.trim() ? 'ใส่ host หรือ IP' : undefined
  const portErr = !(p.port >= 1 && p.port <= 65535) ? '1–65535' : undefined

  return (
    <section aria-label={`แก้ไข ${p.name}`} className="flex h-full min-w-0 flex-1 flex-col rounded-card bg-surface">
      <header className="flex flex-wrap items-center gap-x-4 gap-y-2 px-6 pb-4 pt-5">
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="truncate text-lg font-semibold leading-tight">{p.name || 'ไม่มีชื่อ'}</h2>
            <Badge>{p.protocol}</Badge>
            {!p.enabled && <Badge tone="warn">ปิดใช้งาน</Badge>}
          </div>
          <p className="mt-0.5 flex items-center gap-2 text-sm text-subtle">
            <span className="font-mono">{p.host ? `${p.host}:${p.port}` : 'ยังไม่ตั้ง host'}</span>
            · <TestPill test={p.test} />
          </p>
        </div>
        <div className="flex items-center gap-1.5">
          <Button className="min-w-36" loading={testing} icon={<Zap className="size-4" />} onClick={() => { touch(); test(p.id) }}>
            {testing ? 'กำลังทดสอบ' : 'ทดสอบ IP/Ping'}
          </Button>
          <IconButton label="ลบ Proxy" onClick={() => setConfirm(true)}><Trash2 className="size-[18px]" /></IconButton>
        </div>
      </header>

      <div className="scroll-y min-h-0 flex-1 px-6 pb-6">
        <TestResult p={p} />

        <div className="mt-6 grid grid-cols-6 gap-x-4 gap-y-4">
          <Field label="ชื่อ" className="col-span-6"><Input value={p.name} onChange={(e) => patch(p.id, { name: e.target.value })} /></Field>
          <Field label="โพรโทคอล" className="col-span-6 sm:col-span-2">
            <Select value={p.protocol} onChange={(e) => patch(p.id, { protocol: e.target.value as ProxyProtocol })}>
              {protocols.map((x) => <option key={x} value={x}>{x === 'Socks5' ? 'SOCKS5' : x.toUpperCase()}</option>)}
            </Select>
          </Field>
          <Field label="Host" error={hostErr} className="col-span-4 sm:col-span-3">
            <Input mono value={p.host} onChange={(e) => patch(p.id, { host: e.target.value })} onBlur={touch} aria-invalid={!!hostErr} placeholder="203.0.113.21" />
          </Field>
          <Field label="Port" error={portErr} className="col-span-2 sm:col-span-1">
            <NumberInput value={p.port} min={1} max={65535} onChange={(n) => patch(p.id, { port: n })} aria-invalid={!!portErr} />
          </Field>
          <Field label="Username" hint="ว่าง = ไม่ใช้ยืนยันตัวตน" className="col-span-6 sm:col-span-3">
            <Input value={p.username} onChange={(e) => patch(p.id, { username: e.target.value })} autoComplete="off" />
          </Field>
          <Field label="Password" className="col-span-6 sm:col-span-3">
            <SecretInput value={p.password} onChange={(e) => patch(p.id, { password: e.target.value })} />
          </Field>
        </div>

        <div className="mt-2">
          <BindAccounts proxy={p} />
        </div>

        <details className="group mt-1 text-sm">
          <summary className="cursor-pointer select-none text-[13px] text-subtle transition-colors hover:text-fg">Native Proxy Bridge ทำงานอย่างไร</summary>
          <ul className="mt-2 flex list-disc flex-col gap-1 pl-5 text-muted">
            <li>เปิดพอร์ต <span className="font-mono text-[13px]">127.0.0.1</span> ต่อบัญชี แล้วส่งให้ Studio ผ่าน <span className="font-mono text-[13px]">--proxy-server</span></li>
            <li>เลือกตามลำดับ: Proxy เฉพาะบอท → Proxy จากรายการนี้ → เชื่อมต่อตรง</li>
            <li>บัญชีที่รันอยู่ต้องหยุดแล้วเริ่มใหม่ ค่าใหม่จึงมีผลกับ Studio</li>
          </ul>
        </details>
      </div>

      <Confirm
        open={confirm} title={`ลบ ${p.name || 'Proxy นี้'}?`} confirmLabel="ลบ Proxy"
        detail={usedBy > 0 ? `${usedBy} บัญชีที่ใช้อยู่จะกลับไปเชื่อมต่อตรง · ย้อนกลับไม่ได้` : 'ย้อนกลับไม่ได้'}
        onCancel={() => setConfirm(false)}
        onConfirm={() => { setConfirm(false); remove(p.id); toast({ tone: 'info', title: `ลบ ${p.name || 'Proxy'} แล้ว` }) }}
      />
    </section>
  )
}
