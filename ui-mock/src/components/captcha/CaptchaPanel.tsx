import { memo, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { ChevronRight, Download, FileCode, Eye, LogIn, MonitorUp, Play, Radar, ScanSearch, Timer, Volume2, VolumeX, X, Wifi, Crosshair, Send, Zap } from 'lucide-react'
import { useApp } from '@/lib/store'
import { ago, captchaLabel, displayName, statusMeta, useNow } from '@/lib/format'
import { cn } from '@/lib/cn'
import type { AppSettings, CaptchaRecord, Instance } from '@/lib/types'
import { Avatar, Badge, Button, CopyButton, EmptyState, IconButton, Meter, Skeleton, Stat, StatusDot, Switch } from '@/components/ui/base'
import { Field, Slider, ToggleRow } from '@/components/ui/form'
import { DataTable, Section, tdc } from '@/components/ui/layout'
import { CaptchaPreview } from './CaptchaPreview'

/* ───────── ค่าคงที่และตัวช่วย ───────── */
const TOTAL = 40
const QUOTA_PER_ACCOUNT = 200
const modeLabel: Record<CaptchaRecord['mode'], string> = { 'ai-auto': 'AI อัตโนมัติ', 'ai-manual': 'AI กดเอง', manual: 'มือ' }
const resultMeta: Record<CaptchaRecord['result'], { label: string; tone: 'ok' | 'bad' | 'neutral' }> = {
  solved: { label: 'ผ่าน', tone: 'ok' }, failed: { label: 'ไม่ผ่าน', tone: 'bad' }, dismissed: { label: 'ปิดเอง', tone: 'neutral' },
}

/** บันทึกการตั้งค่าทันที (มี toast จาก store) */
function save(patch: Partial<AppSettings>) {
  const s = useApp.getState()
  s.saveSettings({ ...s.settings, ...patch })
}

/** ค่าที่ลากต่อเนื่อง: เก็บร่างในเครื่อง แล้วค่อยบันทึกเมื่อหยุดลาก (กัน toast รัว) */
function useDraft(value: number, commit: (n: number) => void, ms = 600) {
  const [draft, setDraft] = useState(value)
  const timer = useRef<number | undefined>(undefined)
  useEffect(() => setDraft(value), [value])
  useEffect(() => () => window.clearTimeout(timer.current), [])
  return [draft, (n: number) => {
    setDraft(n)
    window.clearTimeout(timer.current)
    timer.current = window.setTimeout(() => commit(n), ms)
  }] as const
}

/** โหลดข้อมูลครั้งแรกของเซสชัน — แสดง Skeleton ขนาดตรงของจริงสั้นๆ แล้วไม่แสดงอีก */
let warmed = false
function useWarm(ms = 650) {
  const [ready, setReady] = useState(warmed)
  useEffect(() => {
    if (warmed) return
    const t = window.setTimeout(() => { warmed = true; setReady(true) }, ms)
    return () => window.clearTimeout(t)
  }, [ms])
  return ready
}

const quotaBase = (id: string) => 22 + ([...id].reduce((a, c) => a + c.charCodeAt(0), 0) % 21)

function Ago({ t }: { t: number }) {
  const now = useNow(15_000)
  return <>{ago(t, now)}</>
}

/* ───────── สถานะปัจจุบัน (โฟกัสหลักของแผง) ───────── */
const StatusCard = memo(function StatusCard({ inst }: { inst: Instance }) {
  const cap = inst.captcha
  const autoSolve = useApp((s) => s.settings.autoSolve)
  const autoDetect = useApp((s) => s.settings.autoDetect)
  const delay = useApp((s) => s.settings.autoSolveDelay)
  const solve = useApp((s) => s.solveCaptcha)
  const dismiss = useApp((s) => s.dismissCaptcha)
  const start = useApp((s) => s.startInstance)
  const openLogin = useApp((s) => s.openLogin)
  const setHidden = useApp((s) => s.setWindowHidden)
  const toast = useApp((s) => s.toast)
  const quotaFull = useApp((s) => s.instances.filter((i) => i.status === 'running' || i.status === 'starting').length >= s.license.maxAccounts)

  const [solvingId, setSolvingId] = useState<string | null>(null)
  const [peek, setPeek] = useState(false)
  const [scanning, setScanning] = useState(false)
  const [lastScan, setLastScan] = useState<number | null>(null)
  const scanTimer = useRef<number | undefined>(undefined)
  useEffect(() => () => window.clearTimeout(scanTimer.current), [])

  const running = inst.status === 'running'
  const starting = inst.status === 'starting'
  const solving = !!cap && solvingId === cap.id
  const marker = !!cap && cap.kind !== 'inactivity' && (peek || solving)
  const remain = cap ? Math.max(0, delay - (TOTAL - cap.countdown)) : 0
  const tone = !cap ? 'neutral' : cap.countdown <= 10 ? 'bad' : cap.countdown <= 20 ? 'warn' : 'info'

  const onScan = () => {
    setScanning(true)
    scanTimer.current = window.setTimeout(() => {
      setScanning(false)
      setLastScan(Date.now())
      toast({ tone: 'info', title: 'ไม่พบ Captcha', detail: displayName(inst) })
    }, 1200)
  }

  return (
    <div>
      {cap ? (
        <div className="relative">
          <CaptchaPreview kind={cap.kind} seed={cap.sessionId} countdown={cap.countdown} showSolution={marker} />
          {cap.kind !== 'inactivity' && (
            <span className="absolute right-2 top-2">
              <IconButton size="sm" variant="secondary" label="แสดงตำแหน่งที่ solver คำนวณ" aria-pressed={marker} disabled={solving} onClick={() => setPeek((v) => !v)} className={cn(marker && 'bg-primary-soft text-primary')}>
                <Crosshair className="size-4" />
              </IconButton>
            </span>
          )}
        </div>
      ) : (
        <div className="grid aspect-[16/10] w-full place-items-center rounded-ctl bg-surface-2 px-6 text-center">
          <div className="flex flex-col items-center">
            <span className="mb-2 grid size-10 place-items-center rounded-2xl bg-surface-3 text-subtle [&>svg]:size-5">
              {running ? <Radar aria-hidden /> : <ScanSearch aria-hidden />}
            </span>
            <p className="text-sm font-medium">
              {running ? 'กำลังเฝ้าตรวจ' : starting ? 'กำลังเริ่มบัญชี' : !inst.loggedIn ? 'ยังไม่เข้าสู่ระบบ' : 'บัญชีหยุดอยู่'}
            </p>
            <p className="mt-0.5 text-xs text-subtle">
              {running ? (autoDetect ? 'Auto Detect สแกนทุก 2 วินาที' : 'Auto Detect ปิดอยู่ · สแกนด้วยมือ') : 'เริ่มบัญชีเพื่อให้ตรวจ Captcha'}
            </p>
          </div>
        </div>
      )}

      {/* แถวสถานะ: ชนิด + Session + นับถอยหลัง (สงวนความสูงไว้ ไม่ขยับเมื่อสลับ) */}
      <div className="mt-3 flex h-10 items-center justify-between gap-3">
        {cap ? (
          <div className="flex min-w-0 items-center gap-2">
            <Badge tone="bad" dot>{captchaLabel[cap.kind]}</Badge>
            <span className="min-w-0 truncate font-mono text-xs text-muted" title={`Session ${cap.sessionId}`}>{cap.sessionId}</span>
            <CopyButton text={cap.sessionId} label="คัดลอก Session ID" />
          </div>
        ) : (
          <div className="flex items-center gap-2">
            <Badge tone={running ? 'ok' : 'neutral'} dot>{running ? 'ปกติ' : statusMeta[inst.status].label}</Badge>
            <span className="text-xs text-subtle">ไม่มี Captcha</span>
          </div>
        )}
        <div className="shrink-0 text-right leading-none" aria-live="off">
          {cap ? (
            <span className={cn('text-2xl font-semibold tnum', tone === 'bad' ? 'text-bad' : tone === 'warn' ? 'text-warn' : 'text-fg')}>
              {cap.countdown}<span className="ml-1 text-xs font-normal text-subtle">วิ.</span>
            </span>
          ) : <span className="text-2xl font-semibold text-subtle">—</span>}
        </div>
      </div>
      <Meter value={cap ? cap.countdown : 0} max={TOTAL} tone={tone === 'neutral' ? 'info' : tone} />
      <p className="mt-2 flex h-5 items-center gap-1.5 text-xs text-subtle">
        {cap ? (
          <>
            <Timer className="size-3.5 shrink-0" aria-hidden />
            {!autoSolve ? 'Auto Solve ปิดอยู่ · แก้ด้วยมือ' : solving ? 'กำลังแก้…' : remain > 0 ? `Auto Solve ลงมือใน ${remain} วินาที` : 'Auto Solve กำลังลงมือ'}
          </>
        ) : running ? (
          <>สแกนล่าสุด {lastScan ? <Ago t={lastScan} /> : 'ตอนเริ่มบัญชี'}</>
        ) : null}
      </p>

      {/* ปุ่มคงตำแหน่ง: ปุ่มหลักช่องเดียว ปุ่มรองสองช่อง */}
      <div className="mt-3 flex flex-col gap-2">
        {cap ? (
          <Button variant="primary" className="w-full" loading={solving} icon={<Zap className="size-4" />}
            onClick={() => { setSolvingId(cap.id); solve(inst.id, 'ai-manual') }}>
            {solving ? 'กำลังแก้…' : 'แก้ทันที (AI Server)'}
          </Button>
        ) : running ? (
          <Button variant="primary" className="w-full" loading={scanning} icon={<ScanSearch className="size-4" />} onClick={onScan}>
            {scanning ? 'กำลังสแกน…' : 'สแกนเดี๋ยวนี้'}
          </Button>
        ) : !inst.loggedIn ? (
          <Button variant="primary" className="w-full" loading={starting} icon={<LogIn className="size-4" />} onClick={() => openLogin(inst.id)}>เข้าสู่ระบบ</Button>
        ) : (
          <Button variant="primary" className="w-full" loading={starting} disabled={quotaFull} title={quotaFull ? 'เปิดครบโควต้าสูงสุดแล้ว' : undefined}
            icon={<Play className="size-3.5 fill-current" />} onClick={() => start(inst.id)}>
            {starting ? 'กำลังเริ่ม…' : 'เริ่มบัญชี'}
          </Button>
        )}
        <div className="grid grid-cols-2 gap-2">
          <Button disabled={!running || !inst.windowHidden} title={!running ? 'ต้องเริ่มบัญชีก่อน' : !inst.windowHidden ? 'หน้าต่างแสดงอยู่แล้ว' : undefined}
            icon={<MonitorUp className="size-4" />}
            onClick={() => { setHidden(inst.id, false); toast({ tone: 'ok', title: 'ดึงหน้าต่างขึ้นหน้าแล้ว', detail: displayName(inst) }) }}>
            ดึงหน้าต่างขึ้นหน้า
          </Button>
          <Button variant="ghost" disabled={!cap || solving} icon={<X className="size-4" />} onClick={() => cap && dismiss(inst.id)}>ปิด Captcha</Button>
        </div>
      </div>
    </div>
  )
})

/* ───────── ตรวจจับและแก้อัตโนมัติ ───────── */
const AutoSection = memo(function AutoSection() {
  const s = useApp((st) => st.settings)
  const [delay, setDelay] = useDraft(s.autoSolveDelay, (n) => save({ autoSolveDelay: n }))
  return (
    <Section title="ตรวจจับและแก้อัตโนมัติ" aside={<span className="text-xs text-subtle">ใช้กับทุกบัญชี</span>}>
      <div className="flex flex-col divide-y-0">
        <ToggleRow title="Auto Detect" sub="สแกนทุก 2 วินาทีขณะบัญชีทำงาน">
          <Switch label="Auto Detect" checked={s.autoDetect} onChange={(v) => save({ autoDetect: v })} />
        </ToggleRow>
        <ToggleRow title="Auto Solve" sub={s.autoSolve ? `แก้เองหลังพบ ${delay} วินาที` : 'ปิดอยู่ · แก้ด้วยมือ'}>
          <Switch label="Auto Solve" checked={s.autoSolve} onChange={(v) => save({ autoSolve: v })} />
        </ToggleRow>
        <div className={cn('py-2 transition-opacity', !s.autoSolve && 'opacity-50')}>
          <Field label="หน่วงก่อนแก้ (วินาที)" hint="หน่วงทุกครั้งก่อนส่งภาพไป Solver รวมถึงตอนกดแก้เอง · 0–30 วินาที">
            <Slider label="หน่วงก่อนแก้" min={0} max={30} value={delay} onChange={setDelay} />
          </Field>
        </div>
        <ToggleRow title="Auto Copy" sub="คัดลอก Session ID เมื่อพบ Captcha">
          <Switch label="Auto Copy" checked={s.autoCopy} onChange={(v) => save({ autoCopy: v })} />
        </ToggleRow>
      </div>
    </Section>
  )
})

/* ───────── Solver + โควต้า ───────── */
type Probe = { state: 'ok' | 'fail' | 'testing'; ms: number; at: number }
let lastProbe: Probe = { state: 'ok', ms: 84, at: Date.now() - 90_000 }

const SolverSection = memo(function SolverSection({ instanceId, scope }: { instanceId: string; scope: 'instance' | 'system' }) {
  const ready = useWarm()
  const endpoint = useApp((s) => s.settings.solverEndpoint)
  const max = useApp((s) => s.license.maxAccounts)
  const used = useApp((s) => quotaBase(instanceId) + s.history.filter((h) => h.instanceId === instanceId && h.result !== 'dismissed').length)
  const usedAll = useApp((s) => s.instances.reduce((a, i) => a + quotaBase(i.id) + s.history.filter((h) => h.instanceId === i.id && h.result !== 'dismissed').length, 0))
  const [probe, setProbe] = useState<Probe>(lastProbe)
  const timer = useRef<number | undefined>(undefined)
  useEffect(() => () => window.clearTimeout(timer.current), [])

  const test = () => {
    setProbe((p) => ({ ...p, state: 'testing' }))
    timer.current = window.setTimeout(() => {
      const ok = /^https?:\/\//.test(endpoint.trim())
      lastProbe = { state: ok ? 'ok' : 'fail', ms: ok ? 60 + Math.round(Math.random() * 80) : 0, at: Date.now() }
      setProbe(lastProbe)
    }, 1100)
  }
  const host = endpoint.replace(/^https?:\/\//, '')
  const tone = probe.state === 'ok' ? 'ok' : probe.state === 'fail' ? 'bad' : 'warn'

  return (
    <Section title="Solver">
      {!ready ? (
        <div aria-busy="true" aria-label="กำลังโหลด">
          <div className="flex h-10 items-center justify-between gap-3"><Skeleton className="h-4 w-48" /><Skeleton className="h-8 w-36" /></div>
          <Skeleton className="mt-4 h-4 w-40" /><Skeleton className="mt-2 h-1.5 w-full" />
        </div>
      ) : (
        <>
          <div className="flex min-h-10 items-center justify-between gap-3">
            <div className="flex min-w-0 items-center gap-2.5">
              <StatusDot tone={tone} pulse={probe.state === 'testing'} />
              <div className="min-w-0">
                <p className="truncate font-mono text-[13px]" title={endpoint}>{host}</p>
                <p className="text-xs text-subtle">
                  {probe.state === 'testing' ? 'กำลังทดสอบ…' : probe.state === 'ok' ? <>พร้อมใช้ · {probe.ms} ms · <Ago t={probe.at} /></> : 'เชื่อมต่อไม่ได้ · ตรวจ endpoint ในตั้งค่า'}
                </p>
              </div>
            </div>
            <Button size="sm" className="min-w-36" loading={probe.state === 'testing'} icon={<Wifi className="size-4" />} onClick={test}>
              {probe.state === 'testing' ? 'กำลังทดสอบ' : probe.state === 'fail' ? 'ลองใหม่' : 'ทดสอบเชื่อมต่อ AI'}
            </Button>
          </div>
          <div className="mt-4">
            <div className="mb-1.5 flex items-baseline justify-between gap-3 text-xs">
              <span className="text-muted">โควต้า Captcha วันนี้{scope === 'system' ? ' (บัญชีนี้)' : ''}</span>
              <span className="tnum"><b className="text-sm font-semibold text-fg">{used}</b><span className="text-subtle"> / {QUOTA_PER_ACCOUNT} · รีเซ็ต 07:00</span></span>
            </div>
            <Meter value={used} max={QUOTA_PER_ACCOUNT} tone={used / QUOTA_PER_ACCOUNT > 0.9 ? 'bad' : used / QUOTA_PER_ACCOUNT > 0.7 ? 'warn' : 'info'} />
            {scope === 'system' && (
              <>
                <div className="mb-1.5 mt-3 flex items-baseline justify-between gap-3 text-xs">
                  <span className="text-muted">รวมทั้งระบบ ({max} จอ × {QUOTA_PER_ACCOUNT})</span>
                  <span className="tnum"><b className="text-sm font-semibold text-fg">{usedAll}</b><span className="text-subtle"> / {max * QUOTA_PER_ACCOUNT}</span></span>
                </div>
                <Meter value={usedAll} max={max * QUOTA_PER_ACCOUNT} tone="info" />
              </>
            )}
          </div>
        </>
      )}
    </Section>
  )
})

/* ───────── ประวัติการแก้ ───────── */
const HistRow = memo(function HistRow({ rec, now, showAcc }: { rec: CaptchaRecord; now: number; showAcc: boolean }) {
  const acc = useApp((s) => (showAcc ? displayName(s.instances.find((i) => i.id === rec.instanceId) ?? ({ nickname: '', name: rec.instanceId } as Instance)) : ''))
  const r = resultMeta[rec.result]
  return (
    <tr className="cv-auto whitespace-nowrap transition-colors hover:bg-surface-2/60">
      <td className={cn(tdc, 'text-muted')}>{ago(rec.at, now)}</td>
      <td className={tdc}>
        <span className="font-medium">{captchaLabel[rec.kind]}</span>
        <span className="block max-w-32 truncate text-xs text-subtle">{showAcc ? `${acc} · ` : ''}{modeLabel[rec.mode]}</span>
      </td>
      <td className={tdc}><Badge tone={r.tone}>{r.label}</Badge></td>
      <td className={cn(tdc, 'text-right tnum text-muted')}>{rec.ms ? rec.ms.toLocaleString('en-US') : '—'}</td>
    </tr>
  )
})

const HistorySection = memo(function HistorySection({ instanceId, scope }: { instanceId: string; scope: 'instance' | 'system' }) {
  const history = useApp((s) => s.history)
  const now = useNow(30_000)
  const [all, setAll] = useState(false)
  const [more, setMore] = useState(false)
  const showAcc = scope === 'system' && all
  const list = history.filter((h) => all || h.instanceId === instanceId)
  const solved = list.filter((h) => h.result === 'solved')
  const decided = solved.length + list.filter((h) => h.result === 'failed').length
  const rate = decided ? Math.round((solved.length / decided) * 100) : null
  const avg = solved.length ? solved.reduce((a, h) => a + h.ms, 0) / solved.length : null
  const rows = list.slice(0, more ? 200 : 12)

  return (
    <Section
      title="ประวัติการแก้"
      aside={scope === 'system' && (
        <div role="radiogroup" aria-label="ขอบเขตประวัติ" className="inline-flex rounded-ctl bg-surface-2 p-0.5">
          {([[false, 'บัญชีนี้'], [true, 'ทั้งหมด']] as const).map(([v, l]) => (
            <button key={l} type="button" role="radio" aria-checked={all === v} onClick={() => setAll(v)}
              className={cn('h-6 rounded-[8px] px-2.5 text-xs font-medium transition-colors', all === v ? 'bg-surface text-fg shadow-sm' : 'text-muted hover:text-fg')}>{l}</button>
          ))}
        </div>
      )}
    >
      <div className="grid grid-cols-3 gap-4 pb-3">
        <Stat label="อัตราผ่าน" value={rate == null ? '—' : `${rate}%`} sub={decided ? `${solved.length} จาก ${decided}` : undefined} tone={rate == null ? undefined : rate >= 80 ? 'ok' : rate < 50 ? 'bad' : 'warn'} />
        <Stat label="เวลาเฉลี่ย" value={avg == null ? '—' : `${(avg / 1000).toFixed(1)} วิ.`} sub="เฉพาะที่ผ่าน" />
        <Stat label="ทั้งหมด" value={list.length} sub="ครั้ง" />
      </div>
      {rows.length === 0 ? (
        <EmptyState icon={<ScanSearch />} title="ยังไม่มีประวัติ" hint="รายการจะขึ้นเมื่อมี Captcha ถูกแก้หรือปิด" />
      ) : (
        <>
          <div className="overflow-x-auto rounded-ctl bg-surface-2/40">
            <DataTable head={[{ label: 'เวลา' }, { label: 'ชนิด · โหมด' }, { label: 'ผลลัพธ์' }, { label: 'ms', className: 'text-right' }]} className="[&_th]:bg-transparent">
              {rows.map((r) => <HistRow key={r.id} rec={r} now={now} showAcc={showAcc} />)}
            </DataTable>
          </div>
          {list.length > 12 && (
            <Button size="sm" variant="ghost" className="mt-2" onClick={() => setMore((v) => !v)}>{more ? 'แสดงน้อยลง' : `แสดงทั้งหมด (${Math.min(list.length, 200)})`}</Button>
          )}
        </>
      )}
    </Section>
  )
})

/* ───────── ขั้นสูง (พับ) ───────── */
const AdvancedSection = memo(function AdvancedSection({ capId, kind, sessionId }: { capId?: string; kind?: string; sessionId?: string }) {
  const s = useApp((st) => st.settings)
  const toast = useApp((st) => st.toast)
  const nav = useNavigate()
  const [vol, setVol] = useDraft(s.soundVolume, (n) => save({ soundVolume: n }))
  const [silenced, setSilenced] = useState(false)
  useEffect(() => setSilenced(false), [capId])

  const html = `<div id="captcha-verify-container-main-page" data-type="${kind ?? ''}" data-session="${sessionId ?? ''}">…</div>`
  const tg = [['พบ', s.telegramOnDetected], ['สำเร็จ', s.telegramOnSolved], ['ล้มเหลว', s.telegramOnFailed], ['แนบรูป', s.telegramSendPhoto]] as const

  return (
    <details className="group py-4">
      <summary className="flex cursor-pointer list-none items-center gap-1.5 rounded-ctl py-1 text-[13px] font-semibold uppercase tracking-wide text-subtle hover:text-fg [&::-webkit-details-marker]:hidden">
        <ChevronRight className="size-4 transition-transform group-open:rotate-90" aria-hidden />ขั้นสูง
      </summary>
      <div className="pt-3">
        <div className="flex flex-wrap gap-2">
          <Button size="sm" disabled={!capId} title={!capId ? 'ใช้ได้เมื่อมี Captcha' : undefined} icon={<FileCode className="size-4" />}
            onClick={() => { void navigator.clipboard?.writeText(html); toast({ tone: 'ok', title: 'คัดลอก Full HTML แล้ว', detail: `${html.length} ตัวอักษร` }) }}>คัดลอก Full HTML</Button>
          <Button size="sm" disabled={!capId} title={!capId ? 'ใช้ได้เมื่อมี Captcha' : undefined} icon={<Download className="size-4" />}
            onClick={() => toast({ tone: 'ok', title: 'ส่งออกไฟล์แล้ว', detail: `captcha_${sessionId}.html · ${s.captchaFolder.split('\\').slice(-2).join('\\')}` })}>ส่งออกไฟล์</Button>
        </div>
        <ToggleRow title="บันทึกภาพ Captcha" sub="เก็บภาพทุกครั้งที่พบ">
          <Switch label="บันทึกภาพ Captcha" checked={s.saveCaptchaImage} onChange={(v) => save({ saveCaptchaImage: v })} />
        </ToggleRow>

        <p className="mb-1 mt-3 text-xs font-medium text-subtle">เสียงเตือน</p>
        <ToggleRow title="เปิดเสียงเตือน" sub="เล่นทันทีที่พบ Captcha">
          <Switch label="เปิดเสียงเตือน" checked={s.soundEnabled} onChange={(v) => save({ soundEnabled: v })} />
        </ToggleRow>
        <ToggleRow title="เล่นวนซ้ำ" sub="วนจนกว่า Captcha จะหายไป">
          <Switch label="เล่นวนซ้ำ" checked={s.soundLoop} disabled={!s.soundEnabled} onChange={(v) => save({ soundLoop: v })} />
        </ToggleRow>
        <div className={cn('py-2 transition-opacity', !s.soundEnabled && 'opacity-50')}>
          <Field label="ความดัง"><Slider label="ความดังเสียงเตือน" value={vol} onChange={setVol} /></Field>
        </div>
        <div className="flex flex-wrap gap-2">
          <Button size="sm" disabled={!s.soundEnabled} icon={<Volume2 className="size-4" />} onClick={() => toast({ tone: 'info', title: 'ทดสอบเสียงเตือน', detail: `ความดัง ${s.soundVolume}%` })}>ทดสอบเสียง</Button>
          <Button size="sm" variant="danger" disabled={silenced} icon={<VolumeX className="size-4" />}
            onClick={() => { setSilenced(true); toast({ tone: 'warn', title: 'ปิดเสียงเตือนแล้ว', detail: 'หยุดเสียงที่กำลังเล่นทั้งหมด' }) }}>
            {silenced ? 'ปิดเสียงแล้ว' : 'ปิดเสียงเตือนฉุกเฉิน'}
          </Button>
        </div>

        <div className="mt-4 flex items-center justify-between gap-3">
          <div className="flex min-w-0 flex-wrap items-center gap-1.5">
            <span className="mr-1 text-xs font-medium text-subtle">Telegram</span>
            {s.telegramEnabled ? tg.map(([l, on]) => <Badge key={l} tone={on ? 'ok' : 'neutral'}>{l}</Badge>) : <Badge>ปิดอยู่</Badge>}
          </div>
          <Button size="sm" variant="ghost" icon={<Send className="size-4" />} onClick={() => nav('/settings')}>ตั้งค่า</Button>
        </div>
      </div>
    </details>
  )
})

/* ───────── แผงรวม ───────── */
/** `scope="instance"` = แท็บ Captcha ของบัญชี · `scope="system"` = ฝั่งขวาของหน้าตรวจจับทั้งระบบ */
export function CaptchaPanel({ instance, scope = 'instance' }: { instance: Instance; scope?: 'instance' | 'system' }) {
  const nav = useNavigate()
  const cap = instance.captcha
  const meta = statusMeta[instance.status]
  return (
    <div className="@container flex h-full min-h-0 flex-col">
      {scope === 'system' && (
        <header className="flex items-center gap-3 px-6 pb-1 pt-5">
          <Avatar name={instance.loggedIn ? displayName(instance) : '?'} hue={instance.hue} size={40} />
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2">
              <h2 className="truncate text-base font-semibold leading-tight">{instance.loggedIn ? displayName(instance) : instance.name}</h2>
              <Badge tone={meta.tone} dot>{meta.label}</Badge>
            </div>
            <p className="truncate text-xs text-subtle">{instance.loggedIn ? `@${instance.handle}` : 'ยังไม่เข้าสู่ระบบ'} · CDP {instance.cdpPort}</p>
          </div>
          <Button size="sm" variant="ghost" icon={<Eye className="size-4" />} onClick={() => nav(`/accounts/${instance.id}`)}>เปิดบัญชี</Button>
        </header>
      )}
      <div className="flex min-h-0 flex-1 flex-col overflow-y-auto overscroll-contain @[700px]:flex-row @[700px]:overflow-hidden">
        <div className={cn('shrink-0 px-6 pb-2 @[700px]:w-[340px] @[700px]:overflow-y-auto @[700px]:overscroll-contain', scope === 'system' ? 'pt-3' : 'pt-5')}>
          <StatusCard inst={instance} />
        </div>
        <div className="min-w-0 px-6 pb-6 @[700px]:flex-1 @[700px]:overflow-y-auto @[700px]:overscroll-contain">
          <AutoSection />
          <SolverSection instanceId={instance.id} scope={scope} />
          <HistorySection instanceId={instance.id} scope={scope} />
          <AdvancedSection capId={cap?.id} kind={cap?.kind} sessionId={cap?.sessionId} />
        </div>
      </div>
    </div>
  )
}
