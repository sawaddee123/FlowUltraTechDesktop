import { useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router-dom'
import { ChevronDown, Trash2 } from 'lucide-react'
import { useShallow } from 'zustand/react/shallow'
import { useApp } from '@/lib/store'
import type { AppSettings } from '@/lib/types'
import { Badge, Button, IconButton, Segmented, Spinner, Switch } from '@/components/ui/base'
import { Field, Input, Textarea, ToggleRow } from '@/components/ui/form'
import { cn } from '@/lib/cn'

function Card({ title, sub, aside, children, className }: { title: string; sub?: string; aside?: ReactNode; children?: ReactNode; className?: string }) {
  return (
    <section aria-label={title} className={cn('flex flex-col gap-3 rounded-card bg-surface px-5.5 py-5', className)}>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="min-w-0 flex-[1_1_220px]">
          <h2 className="text-base font-semibold">{title}</h2>
          {sub && <p className="mt-0.5 text-[13px] text-muted">{sub}</p>}
        </div>
        {aside}
      </div>
      {children}
    </section>
  )
}

/** ตั้งค่าหน้าเดียว — บันทึกทันทีที่เปลี่ยน ค่าที่ระบบจัดให้เองพับไว้ใน "ขั้นสูง" */
export default function SettingsPage() {
  const nav = useNavigate()
  const { settings, license, theme, proxies, used } = useApp(useShallow((s) => ({
    settings: s.settings, license: s.license, theme: s.theme, proxies: s.proxies, used: s.instances.length,
  })))
  const { saveSettings, setTheme, logout, setUpdate, toast, importProxies, removeProxy } = useApp(useShallow((s) => ({
    saveSettings: s.saveSettings, setTheme: s.setTheme, logout: s.logout, setUpdate: s.setUpdate, toast: s.toast,
    importProxies: s.importProxies, removeProxy: s.removeProxy,
  })))
  const set = <K extends keyof AppSettings>(k: K, v: AppSettings[K]) => saveSettings({ ...settings, [k]: v })
  const [paste, setPaste] = useState('')
  const [adv, setAdv] = useState(false)

  const doImport = () => {
    const n = importProxies(paste)
    toast(n ? { tone: 'ok', title: `เพิ่ม Proxy ${n} รายการ`, detail: 'กำลังทดสอบให้อัตโนมัติ' } : { tone: 'warn', title: 'ไม่พบบรรทัดที่ถูกต้อง', detail: 'รูปแบบ host:port:user:pass' })
    if (n) setPaste('')
  }

  return (
    <div className="px-10 pb-12 pt-8 max-[900px]:px-5">
      <div className="flex max-w-[720px] flex-col gap-4">
        <h1 className="mb-2 text-[26px] font-semibold tracking-tight">ตั้งค่า</h1>

        <Card
          title={`แพ็กเกจ ${license.plan}`} sub={`${license.username} · ใช้ ${used} จาก ${license.maxAccounts} บัญชี · หมดอายุ ${license.expiresAt}`}
          aside={
            <div className="flex gap-2">
              <Button className="h-11 px-4.5" onClick={() => toast({ tone: 'info', title: 'เปิดหน้าจัดการแพ็กเกจ' })}>จัดการแพ็กเกจ</Button>
              <Button variant="ghost" className="h-11 px-3.5 text-bad hover:text-bad" onClick={() => { logout(); nav('/login') }}>ออกจากระบบ</Button>
            </div>
          }
        />

        <Card title="แจ้งเตือนเมื่อต้องให้คุณช่วย">
          <ToggleRow title="เสียงเตือน" sub="ดังเฉพาะตอนที่ AI แก้ Captcha ไม่ผ่าน">
            <Switch checked={settings.soundEnabled} onChange={(v) => set('soundEnabled', v)} label="เสียงเตือน" />
          </ToggleRow>
          <ToggleRow title="แจ้งเตือนทาง Telegram" sub={settings.telegramEnabled ? 'เชื่อมแล้ว' : 'กดเชื่อม แล้วกด Start ในแชทบอท ไม่ต้องกรอก Token'}>
            <Button variant={settings.telegramEnabled ? 'secondary' : 'primary'} className="h-11 px-4.5" onClick={() => set('telegramEnabled', !settings.telegramEnabled)}>
              {settings.telegramEnabled ? 'ยกเลิกการเชื่อม' : 'เชื่อม Telegram'}
            </Button>
          </ToggleRow>
        </Card>

        <Card title="Proxy" sub="แต่ละบัญชีได้ IP ไม่ซ้ำกัน และสลับให้เองเมื่อใช้ไม่ได้">
          <Segmented label="Proxy" value={settings.proxyMode} onChange={(v) => set('proxyMode', v)}
            options={[{ value: 'auto', label: 'อัตโนมัติ' }, { value: 'own', label: 'ใช้ของฉัน' }, { value: 'off', label: 'ไม่ใช้' }]} />
          {settings.proxyMode === 'own' && (
            <div className="flex animate-fade-in flex-col gap-3">
              {proxies.length > 0 && (
                <ul className="flex flex-col" aria-label="Proxy ของฉัน">
                  {proxies.map((p) => (
                    <li key={p.id} className="flex items-center gap-3 py-1.5 text-sm">
                      <span className="min-w-0 flex-1 truncate font-mono text-[13px]">{p.host}:{p.port}</span>
                      {p.test === 'testing' ? <Spinner className="text-muted" />
                        : p.test ? <Badge tone={p.test.ok ? 'ok' : 'bad'} dot>{p.test.ok ? `${p.test.pingMs} ms` : 'ใช้ไม่ได้'}</Badge>
                        : <Badge>ยังไม่ทดสอบ</Badge>}
                      <IconButton label={`ลบ ${p.host}`} size="sm" onClick={() => removeProxy(p.id)}><Trash2 className="size-4" /></IconButton>
                    </li>
                  ))}
                </ul>
              )}
              <Field label="วาง Proxy ของคุณ (บรรทัดละตัว host:port:user:pass)">
                <Textarea mono rows={4} value={paste} onChange={(e) => setPaste(e.target.value)} placeholder="1.2.3.4:1080:user:pass" />
              </Field>
              <Button className="self-start" disabled={!paste.trim()} onClick={doImport}>เพิ่มและทดสอบ</Button>
            </div>
          )}
        </Card>

        <Card title="ธีม" aside={
          <Segmented label="ธีม" value={theme} onChange={setTheme} options={[{ value: 'dark', label: 'มืด' }, { value: 'light', label: 'สว่าง' }]} />
        } />

        <section aria-label="ขั้นสูง" className="rounded-card bg-surface">
          <h2>
            <button type="button" aria-expanded={adv} onClick={() => setAdv((a) => !a)}
              className="flex min-h-15 w-full items-center justify-between gap-3 px-5.5 text-left text-base font-semibold">
              ขั้นสูง
              <span className="flex items-center gap-1.5 text-sm font-normal text-muted">
                {adv ? 'ซ่อน' : 'ค่าที่ตั้งให้อัตโนมัติแล้ว'}<ChevronDown className={cn('size-4 transition-transform', adv && 'rotate-180')} aria-hidden />
              </span>
            </button>
          </h2>
          {adv && (
            <div className="flex animate-fade-in flex-col gap-3 px-5.5 pb-5">
              <ToggleRow title="AI แก้ Captcha อัตโนมัติ" sub="ปิดแล้วต้องแก้เองทุกครั้ง">
                <Switch checked={settings.autoSolve} onChange={(v) => set('autoSolve', v)} label="AI แก้ Captcha อัตโนมัติ" />
              </ToggleRow>
              <ToggleRow title="ซ่อนหน้าต่าง Studio หลังเปิด">
                <Switch checked={settings.autoHideOnLaunch} onChange={(v) => set('autoHideOnLaunch', v)} label="ซ่อนหน้าต่าง Studio หลังเปิด" />
              </ToggleRow>
              <ToggleRow title="บันทึกภาพ Captcha">
                <Switch checked={settings.saveCaptchaImage} onChange={(v) => set('saveCaptchaImage', v)} label="บันทึกภาพ Captcha" />
              </ToggleRow>
              <Field label="ที่อยู่ TikTok LIVE Studio" hint="หาให้อัตโนมัติ แก้เฉพาะเมื่อติดตั้งไว้ที่อื่น">
                <Input mono value={settings.studioPath} onChange={(e) => set('studioPath', e.target.value)} />
              </Field>
              <p className="text-[13px] text-muted">โฟลเดอร์ พอร์ต และ Proxy ของแต่ละบัญชีจัดให้อัตโนมัติ ดูรายบัญชีได้ที่ บัญชี › ขั้นสูง</p>
            </div>
          )}
        </section>

        <p className="mt-2 text-[13px] text-muted">
          เวอร์ชัน 1.7.0 · อัปเดตเองเมื่อเปิดโปรแกรมครั้งถัดไป ·{' '}
          <button type="button" className="text-primary hover:underline" onClick={() => setUpdate({ open: true, mandatory: false })}>ตรวจอัปเดต</button>
        </p>
      </div>
    </div>
  )
}
