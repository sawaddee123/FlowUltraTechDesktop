import { useState } from 'react'
import { ExternalLink, Eye, EyeOff, FlaskConical, RotateCw, TriangleAlert } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import type { BotProxy, Instance, ProxyItem, ProxyProtocol } from '@/lib/types'
import { useApp } from '@/lib/store'
import { ago } from '@/lib/format'
import { Badge, Button, IconButton, Segmented, Spinner } from '@/components/ui/base'
import { Field, Input, NumberInput, Select } from '@/components/ui/form'
import { Section } from '@/components/ui/layout'
import { useTimers } from './hooks'

type Mode = 'direct' | 'pool' | 'bot'
type Bot = Omit<BotProxy, 'enabled'>
interface Draft { mode: Mode; poolId: string; bot: Bot }
type Result = { ok: boolean; ip: string; pingMs: number; at: number }

/** บัญชีที่เพิ่งบันทึก Proxy ขณะทำงาน — รอรีสตาร์ตจึงจะมีผล (คงไว้ข้ามการสลับแท็บ) */
const pendingRestart = new Set<string>()

const fromInstance = (i: Instance): Draft => ({
  mode: i.botProxy.enabled ? 'bot' : i.proxyId ? 'pool' : 'direct',
  poolId: i.proxyId ?? '',
  bot: { protocol: i.botProxy.protocol, host: i.botProxy.host, port: i.botProxy.port, username: i.botProxy.username, password: i.botProxy.password },
})
const sig = (d: Draft) => (d.mode === 'direct' ? 'direct' : d.mode === 'pool' ? `pool:${d.poolId}` : `bot:${JSON.stringify(d.bot)}`)

const MODES: { value: Mode; label: string }[] = [
  { value: 'direct', label: 'เชื่อมต่อตรง' }, { value: 'pool', label: 'Proxy Pool' }, { value: 'bot', label: 'เฉพาะบอทนี้' },
]
const PROTOCOLS: ProxyProtocol[] = ['Socks5', 'Http', 'Https']

function poolLabel(p: ProxyItem) {
  const t = p.test
  const state = !p.enabled ? 'ปิดใช้' : t === 'testing' ? 'กำลังทดสอบ' : !t ? 'ยังไม่ทดสอบ' : t.ok ? `ผ่าน ${t.pingMs} ms` : 'ไม่ผ่าน'
  return `${p.name} · ${p.protocol} ${p.host || '—'}:${p.port} · ${state}`
}

function ResultLine({ res, idle }: { res: Result | 'testing' | null | undefined; idle: string }) {
  if (res === 'testing') return <span className="flex items-center gap-2 text-sm text-muted"><Spinner className="text-primary" />กำลังทดสอบ…</span>
  if (!res) return <span className="text-sm text-subtle">{idle}</span>
  return res.ok ? (
    <span className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
      <Badge tone="ok" dot>ผ่าน</Badge>
      <span className="font-mono text-[13px]">{res.ip}</span>
      <span className="text-subtle tnum">{res.pingMs} ms · {ago(res.at)}</span>
    </span>
  ) : (
    <span className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm">
      <Badge tone="bad" dot>ไม่ผ่าน</Badge>
      <span className="text-subtle">ตรวจ Host/Port แล้วลองใหม่ · {ago(res.at)}</span>
    </span>
  )
}

export function ProxySection({ instance: i }: { instance: Instance }) {
  const nav = useNavigate()
  const proxies = useApp((s) => s.proxies)
  const patchInstance = useApp((s) => s.patchInstance)
  const testProxy = useApp((s) => s.testProxy)
  const toast = useApp((s) => s.toast)
  const stop = useApp((s) => s.stopInstance)
  const start = useApp((s) => s.startInstance)
  const later = useTimers()

  const running = i.status === 'running'
  const saved = fromInstance(i)
  const [draft, setDraft] = useState<Draft>(saved)
  const [showPw, setShowPw] = useState(false)
  const [saving, setSaving] = useState(false)
  const [local, setLocal] = useState<{ mode: Mode; res: Result | 'testing' } | null>(null)
  const [pending, setPending] = useState(() => pendingRestart.has(i.id))

  const pool = proxies.find((p) => p.id === draft.poolId)
  const setBot = (patch: Partial<Bot>) => setDraft((d) => ({ ...d, bot: { ...d.bot, ...patch } }))

  const hostErr = draft.mode === 'bot' && !draft.bot.host.trim() ? 'ใส่ Host' : ''
  const portErr = draft.mode === 'bot' && !(draft.bot.port >= 1 && draft.bot.port <= 65535) ? 'พอร์ต 1–65535' : ''
  const poolErr = draft.mode === 'pool' && !pool
  const invalid = !!hostErr || !!portErr || poolErr
  const dirty = sig(draft) !== sig(saved)

  const poolRes = pool?.test
  const shownRes = draft.mode === 'pool' ? poolRes : local?.mode === draft.mode ? local.res : null
  const testing = shownRes === 'testing'

  const test = () => {
    if (invalid || testing) return
    if (draft.mode === 'pool') { testProxy(draft.poolId); return }
    const mode = draft.mode
    const bot = draft.bot
    setLocal({ mode, res: 'testing' })
    later(() => {
      const ok = mode === 'direct' || (!!bot.host.trim() && bot.port > 0)
      const ip = mode === 'direct' ? '49.228.17.203' : /^\d+\.\d+\.\d+\.\d+$/.test(bot.host.trim()) ? bot.host.trim() : '203.0.113.' + (20 + Math.floor(Math.random() * 60))
      setLocal({ mode, res: { ok, ip: ok ? ip : '', pingMs: ok ? 12 + Math.round(Math.random() * 70) : 0, at: Date.now() } })
    }, 1100)
  }

  const save = () => {
    if (invalid || !dirty || saving) return
    setSaving(true)
    later(() => {
      if (draft.mode === 'direct') patchInstance(i.id, { proxyId: null, botProxy: { ...i.botProxy, enabled: false } })
      else if (draft.mode === 'pool') patchInstance(i.id, { proxyId: draft.poolId, botProxy: { ...i.botProxy, enabled: false } })
      else patchInstance(i.id, { botProxy: { ...draft.bot, host: draft.bot.host.trim(), enabled: true } })
      setSaving(false)
      if (running) { pendingRestart.add(i.id); setPending(true) }
      toast({ tone: 'ok', title: 'บันทึก Proxy แล้ว', detail: running ? 'รีสตาร์ตบัญชีเพื่อให้มีผล' : 'มีผลเมื่อเริ่มบัญชี' })
    }, 450)
  }

  const restart = () => {
    pendingRestart.delete(i.id)
    setPending(false)
    stop(i.id)
    window.setTimeout(() => start(i.id), 500)
  }
  const showPending = running && pending

  return (
    <Section title="Proxy" aside={<span className="text-xs text-subtle">มีผลหลังหยุดแล้วเริ่มบัญชีใหม่</span>}>
      <div className="flex flex-wrap items-center gap-3">
        <Segmented<Mode> label="แหล่ง Proxy" value={draft.mode} onChange={(mode) => setDraft((d) => ({ ...d, mode }))} options={MODES} />
        <div className="ml-auto flex items-center gap-2">
          <Button className="min-w-24" loading={testing} disabled={invalid} icon={<FlaskConical className="size-4" />} onClick={test}>ทดสอบ</Button>
          <Button variant="primary" className="min-w-20" loading={saving} disabled={!dirty || invalid} onClick={save}>บันทึก</Button>
        </div>
      </div>

      <div className="mt-4 min-h-[22px]"><ResultLine res={shownRes} idle={draft.mode === 'pool' && !pool ? 'เลือก Proxy ก่อนทดสอบ' : 'ยังไม่เคยทดสอบ'} /></div>

      <div className="mt-4">
        {draft.mode === 'direct' && (
          <p className="text-sm text-muted">ออกเน็ตด้วยไอพีของเครื่องนี้ ไม่ผ่าน Proxy</p>
        )}

        {draft.mode === 'pool' && (
          <div className="flex max-w-xl flex-col gap-3">
            {proxies.length === 0 ? (
              <p className="text-sm text-muted">ยังไม่มี Proxy ใน Pool — เพิ่มที่หน้า Proxy ก่อน</p>
            ) : (
              <Field label="เลือกจาก Pool" hint="Proxy ที่ปิดใช้งานเลือกไม่ได้ ผลทดสอบมาจากหน้า Proxy จึงตรงกันทุกที่">
                <Select value={draft.poolId} onChange={(e) => setDraft((d) => ({ ...d, poolId: e.target.value }))} aria-invalid={poolErr}>
                  <option value="">— เลือก Proxy —</option>
                  {proxies.map((p) => <option key={p.id} value={p.id} disabled={!p.enabled && p.id !== draft.poolId}>{poolLabel(p)}</option>)}
                </Select>
              </Field>
            )}
            <div>
              <Button size="sm" variant="ghost" icon={<ExternalLink className="size-3.5" />} onClick={() => nav('/proxy')}>จัดการ Proxy Pool</Button>
            </div>
          </div>
        )}

        {draft.mode === 'bot' && (
          <div className="grid max-w-2xl grid-cols-1 gap-x-3 gap-y-3 sm:grid-cols-[130px_minmax(0,1fr)_120px]">
            <Field label="Protocol">
              <Select value={draft.bot.protocol} onChange={(e) => setBot({ protocol: e.target.value as ProxyProtocol })}>
                {PROTOCOLS.map((p) => <option key={p}>{p}</option>)}
              </Select>
            </Field>
            <Field label="Host" error={hostErr}>
              <Input mono value={draft.bot.host} onChange={(e) => setBot({ host: e.target.value })} placeholder="203.0.113.21" spellCheck={false} aria-invalid={!!hostErr} />
            </Field>
            <Field label="Port" error={portErr}>
              <NumberInput value={draft.bot.port} min={1} max={65535} onChange={(port) => setBot({ port })} aria-invalid={!!portErr} />
            </Field>
            <div className="sm:col-span-3 grid grid-cols-1 gap-3 sm:grid-cols-2">
              <Field label="Username">
                <Input value={draft.bot.username} onChange={(e) => setBot({ username: e.target.value })} autoComplete="off" spellCheck={false} />
              </Field>
              <Field label="Password">
                <span className="relative block">
                  <Input type={showPw ? 'text' : 'password'} value={draft.bot.password} onChange={(e) => setBot({ password: e.target.value })} autoComplete="new-password" className="pr-10" />
                  <span className="absolute right-0.5 top-1/2 -translate-y-1/2">
                    <IconButton label={showPw ? 'ซ่อนรหัสผ่าน' : 'แสดงรหัสผ่าน'} size="sm" aria-pressed={showPw} onClick={() => setShowPw((v) => !v)}>
                      {showPw ? <EyeOff className="size-4" /> : <Eye className="size-4" />}
                    </IconButton>
                  </span>
                </span>
              </Field>
            </div>
          </div>
        )}
      </div>

      {showPending && (
        <div role="status" className="mt-4 flex max-w-2xl animate-rise items-center gap-3 rounded-ctl bg-warn-soft px-3.5 py-2 text-sm text-warn">
          <TriangleAlert className="size-4 shrink-0" aria-hidden />
          <span className="min-w-0 flex-1">บันทึกแล้ว · ต้องรีสตาร์ตบัญชีจึงจะใช้ Proxy ใหม่</span>
          <Button size="sm" icon={<RotateCw className="size-3.5" />} onClick={restart}>รีสตาร์ตบัญชี</Button>
        </div>
      )}
    </Section>
  )
}
