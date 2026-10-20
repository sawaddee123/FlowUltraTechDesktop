import { useEffect, useRef, useState, type ReactNode } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useShallow } from 'zustand/react/shallow'
import { Download, LogOut, RefreshCw } from 'lucide-react'
import { useApp } from '@/lib/store'
import type { AppSettings } from '@/lib/types'
import { Badge, Button, Segmented, Skeleton, StatusDot, Switch } from '@/components/ui/base'
import { Field, Input, NumberInput, SecretInput, Slider, ToggleRow } from '@/components/ui/form'
import { cn } from '@/lib/cn'
import { PageHeader } from '@/components/ui/layout'
import { useFirstLoad, useScrollSpy } from '../_pages/hooks'

const sections = [
  { id: 'account', label: 'บัญชีและสิทธิ์ใช้งาน' },
  { id: 'captcha', label: 'Captcha' },
  { id: 'sound', label: 'เสียงแจ้งเตือน' },
  { id: 'telegram', label: 'Telegram' },
  { id: 'paths', label: 'การจัดเก็บ' },
  { id: 'appearance', label: 'หน้าตา' },
  { id: 'updates', label: 'อัปเดต' },
  { id: 'server', label: 'สถานะเซิร์ฟเวอร์' },
]
const ids = sections.map((s) => s.id)

function Block({ id, title, children }: { id: string; title: string; children: ReactNode }) {
  return (
    <section id={id} className="scroll-mt-2 rounded-card bg-surface p-6">
      <h2 className="mb-3 text-base font-semibold">{title}</h2>
      <div className="flex flex-col gap-3">{children}</div>
    </section>
  )
}

function Row({ label, value }: { label: string; value: ReactNode }) {
  return <div className="flex items-center justify-between gap-4 py-1.5 text-sm"><span className="text-muted">{label}</span><span className="min-w-0 truncate text-right font-medium">{value}</span></div>
}

export default function SettingsPage() {
  const loading = useFirstLoad('settings')
  const { section } = useParams()
  const navigate = useNavigate()
  const { settings, license, theme, server } = useApp(useShallow((s) => ({ settings: s.settings, license: s.license, theme: s.theme, server: s.server })))
  const saveSettings = useApp((s) => s.saveSettings)
  const setTheme = useApp((s) => s.setTheme)
  const logout = useApp((s) => s.logout)
  const setUpdate = useApp((s) => s.setUpdate)
  const toast = useApp((s) => s.toast)
  const [draft, setDraft] = useState<AppSettings>(settings)
  const [saving, setSaving] = useState(false)
  const rootRef = useRef<HTMLDivElement>(null)
  const [active, jumpTo] = useScrollSpy(ids, rootRef, section)
  const dirty = JSON.stringify(draft) !== JSON.stringify(settings)
  const set = <K extends keyof AppSettings>(k: K, v: AppSettings[K]) => setDraft((d) => ({ ...d, [k]: v }))

  useEffect(() => {
    if (loading || !section || !ids.includes(section)) return
    jumpTo(section, false)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [loading, section])

  const save = () => {
    setSaving(true)
    window.setTimeout(() => { saveSettings(draft); setSaving(false) }, 600)
  }
  const go = (id: string) => { jumpTo(id); navigate(`/settings/${id}`, { replace: true }) }

  return (
    <div className="flex h-full flex-col">
      <PageHeader title="ตั้งค่า" sub="ค่าทั้งหมดซิงก์กับบัญชีของคุณ" />
      {loading ? (
        <div className="flex min-h-0 flex-1 gap-3 pb-1" aria-busy="true" aria-label="กำลังโหลด">
          <div className="flex w-52 shrink-0 flex-col gap-2 p-2"><Skeleton className="h-8" /><Skeleton className="h-8" /><Skeleton className="h-8" /></div>
          <div className="flex flex-1 flex-col gap-3"><Skeleton className="h-48 w-full" /><Skeleton className="h-64 w-full" /></div>
        </div>
      ) : (
        <div className="flex min-h-0 flex-1 gap-3 pb-1">
          <nav aria-label="หมวดตั้งค่า" className="w-52 shrink-0 max-[1180px]:w-44">
            <ul className="flex flex-col gap-0.5">
              {sections.map((s) => (
                <li key={s.id}>
                  <button
                    type="button" onClick={() => go(s.id)} aria-current={active === s.id ? 'true' : undefined}
                    className={cn('h-9 w-full rounded-ctl px-3 text-left text-sm font-medium transition-colors', active === s.id ? 'bg-primary-soft text-primary' : 'text-muted hover:bg-surface-2 hover:text-fg')}
                  >
                    {s.label}
                  </button>
                </li>
              ))}
            </ul>
          </nav>

          <div className="flex min-w-0 flex-1 flex-col">
            <div ref={rootRef} className="scroll-y flex min-h-0 flex-1 flex-col gap-3 pr-1">
              <Block id="account" title="บัญชีและสิทธิ์ใช้งาน">
                <Row label="ผู้ใช้" value={license.username} />
                <Row label="แพ็กเกจ" value={<Badge tone="info">{license.plan}</Badge>} />
                <Row label="จำนวนบัญชีสูงสุด" value={license.maxAccounts} />
                <Row label="หมดอายุ" value={license.expiresAt} />
                <Row label="HWID" value={<span className="font-mono text-[13px]">{license.hwid}</span>} />
                <div><Button variant="danger" icon={<LogOut className="size-4" />} onClick={logout}>ออกจากระบบ</Button></div>
              </Block>

              <Block id="captcha" title="Captcha">
                <ToggleRow title="แก้อัตโนมัติ" sub="ให้ AI แก้เมื่อตรวจพบ"><Switch checked={draft.autoSolve} onChange={(v) => set('autoSolve', v)} label="แก้อัตโนมัติ" /></ToggleRow>
                <Field label="หน่วงก่อนแก้" hint="รอให้ Captcha โหลดครบก่อนเริ่มแก้" inline>
                  <NumberInput className="w-32" value={draft.autoSolveDelay} min={0} max={60} suffix="วิ." disabled={!draft.autoSolve} onChange={(n) => set('autoSolveDelay', n)} />
                </Field>
                <ToggleRow title="ตรวจจับอัตโนมัติ"><Switch checked={draft.autoDetect} onChange={(v) => set('autoDetect', v)} label="ตรวจจับอัตโนมัติ" /></ToggleRow>
                <ToggleRow title="คัดลอกภาพอัตโนมัติ" sub="ใส่ภาพลงคลิปบอร์ดเมื่อพบ"><Switch checked={draft.autoCopy} onChange={(v) => set('autoCopy', v)} label="คัดลอกภาพอัตโนมัติ" /></ToggleRow>
                <ToggleRow title="บันทึกภาพ Captcha"><Switch checked={draft.saveCaptchaImage} onChange={(v) => set('saveCaptchaImage', v)} label="บันทึกภาพ Captcha" /></ToggleRow>
                <Field label="โฟลเดอร์เก็บภาพ"><Input mono value={draft.captchaFolder} disabled={!draft.saveCaptchaImage} onChange={(e) => set('captchaFolder', e.target.value)} /></Field>
                <Field label="Solver endpoint" hint="ที่อยู่เซิร์ฟเวอร์ที่ใช้แก้ Captcha"><Input mono value={draft.solverEndpoint} onChange={(e) => set('solverEndpoint', e.target.value)} /></Field>
              </Block>

              <Block id="sound" title="เสียงแจ้งเตือน">
                <ToggleRow title="เปิดเสียง"><Switch checked={draft.soundEnabled} onChange={(v) => set('soundEnabled', v)} label="เปิดเสียง" /></ToggleRow>
                <ToggleRow title="เล่นวนจนกว่าจะแก้" sub="หยุดเมื่อ Captcha ถูกแก้"><Switch checked={draft.soundLoop} disabled={!draft.soundEnabled} onChange={(v) => set('soundLoop', v)} label="เล่นวน" /></ToggleRow>
                <Field label="ระดับเสียง"><Slider label="ระดับเสียง" value={draft.soundVolume} onChange={(n) => set('soundVolume', n)} /></Field>
                <Field label="ไฟล์เสียง" hint="เว้นว่างเพื่อใช้เสียงเริ่มต้น"><Input mono placeholder="เสียงเริ่มต้น" value={draft.soundFile} onChange={(e) => set('soundFile', e.target.value)} /></Field>
              </Block>

              <Block id="telegram" title="Telegram">
                <ToggleRow title="ส่งแจ้งเตือนไป Telegram"><Switch checked={draft.telegramEnabled} onChange={(v) => set('telegramEnabled', v)} label="ส่งแจ้งเตือนไป Telegram" /></ToggleRow>
                <Field label="Bot token"><SecretInput mono value={draft.telegramToken} onChange={(e) => set('telegramToken', e.target.value)} /></Field>
                <Field label="Chat ID"><Input mono value={draft.telegramChatId} onChange={(e) => set('telegramChatId', e.target.value)} /></Field>
                <ToggleRow title="แจ้งเมื่อพบ Captcha"><Switch checked={draft.telegramOnDetected} onChange={(v) => set('telegramOnDetected', v)} label="แจ้งเมื่อพบ" /></ToggleRow>
                <ToggleRow title="แนบภาพ"><Switch checked={draft.telegramSendPhoto} onChange={(v) => set('telegramSendPhoto', v)} label="แนบภาพ" /></ToggleRow>
                <ToggleRow title="แจ้งเมื่อแก้สำเร็จ"><Switch checked={draft.telegramOnSolved} onChange={(v) => set('telegramOnSolved', v)} label="แจ้งเมื่อสำเร็จ" /></ToggleRow>
                <ToggleRow title="แจ้งเมื่อแก้ไม่สำเร็จ"><Switch checked={draft.telegramOnFailed} onChange={(v) => set('telegramOnFailed', v)} label="แจ้งเมื่อล้มเหลว" /></ToggleRow>
                <div><Button onClick={() => toast({ tone: 'ok', title: 'ส่งข้อความทดสอบแล้ว', detail: 'ตรวจใน Telegram' })}>ส่งข้อความทดสอบ</Button></div>
              </Block>

              <Block id="paths" title="การจัดเก็บ">
                <Field label="Studio.exe"><Input mono value={draft.studioPath} onChange={(e) => set('studioPath', e.target.value)} /></Field>
                <Field label="Proxifier.exe"><Input mono value={draft.proxifierPath} onChange={(e) => set('proxifierPath', e.target.value)} /></Field>
                <Field label="โปรไฟล์ Proxifier (.ppx)"><Input mono value={draft.proxifierProfile} onChange={(e) => set('proxifierProfile', e.target.value)} /></Field>
                <ToggleRow title="ซ่อนหน้าต่างหลังเปิด" sub="ซ่อน Studio ทันทีที่เริ่มบัญชี"><Switch checked={draft.autoHideOnLaunch} onChange={(v) => set('autoHideOnLaunch', v)} label="ซ่อนหน้าต่างหลังเปิด" /></ToggleRow>
                <Field label="พอร์ต Overlay" hint="พอร์ตในเครื่องที่เสิร์ฟ Overlay ให้ OBS"><NumberInput className="w-40" value={draft.overlayPort} min={1024} max={65535} onChange={(n) => set('overlayPort', n)} /></Field>
              </Block>

              <Block id="appearance" title="หน้าตา">
                <Field label="ธีม" inline>
                  <Segmented label="ธีม" value={theme} onChange={setTheme} options={[{ value: 'dark', label: 'มืด' }, { value: 'light', label: 'สว่าง' }]} />
                </Field>
              </Block>

              <Block id="updates" title="อัปเดต">
                <Row label="เวอร์ชันปัจจุบัน" value={<span className="font-mono text-[13px]">1.6.0</span>} />
                <div className="flex gap-2">
                  <Button icon={<Download className="size-4" />} onClick={() => setUpdate({ open: true, mandatory: false })}>ตรวจสอบอัปเดต</Button>
                </div>
              </Block>

              <Block id="server" title="สถานะเซิร์ฟเวอร์">
                <Row label="การเชื่อมต่อ" value={<span className="inline-flex items-center gap-2"><StatusDot tone={server.connected ? 'ok' : 'bad'} />{server.connected ? 'เชื่อมต่อแล้ว' : 'ขาดการเชื่อมต่อ'}</span>} />
                <Row label="หน่วงเวลา" value={<span className="tnum">{server.latencyMs} ms</span>} />
                <Row label="เซิร์ฟเวอร์" value={<span className="font-mono text-[13px]">{license.server}</span>} />
                <div><Button icon={<RefreshCw className="size-4" />} onClick={() => toast({ tone: 'ok', title: 'เซิร์ฟเวอร์ตอบสนองปกติ', detail: `${server.latencyMs} ms` })}>ทดสอบการเชื่อมต่อ</Button></div>
              </Block>
              <div className="h-4 shrink-0" />
            </div>

            <div className={cn('mt-2 flex shrink-0 items-center justify-between gap-3 rounded-card bg-surface px-5 py-3 shadow-float transition-opacity', !dirty && !saving && 'opacity-60')}>
              <p className="text-sm text-muted">{dirty ? 'มีการแก้ไขที่ยังไม่บันทึก' : 'ไม่มีการแก้ไข'}</p>
              <div className="flex gap-2">
                <Button variant="ghost" disabled={!dirty || saving} onClick={() => setDraft(settings)}>ยกเลิก</Button>
                <Button variant="primary" className="min-w-24" loading={saving} disabled={!dirty} onClick={save}>บันทึก</Button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
