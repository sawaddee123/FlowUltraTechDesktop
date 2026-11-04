import type { ClockMode, OverlayConfig } from '@/lib/types'
import { useApp } from '@/lib/store'
import { useNow } from '@/lib/format'
import { cn } from '@/lib/cn'
import { CopyButton, Segmented, Switch } from '@/components/ui/base'
import { Field, Input, Select } from '@/components/ui/form'
import type { TabProps } from './registry'

const MODES: { value: ClockMode; label: string }[] = [
  { value: 'wall', label: 'นาฬิกาแขวน' }, { value: 'classic', label: 'คลาสสิก' }, { value: 'desk3d', label: 'ตั้งโต๊ะ 3D' },
  { value: 'twinbell', label: 'ระฆังคู่' }, { value: 'led', label: 'LED' }, { value: 'digital', label: 'LED แดง' },
]
const THAI_MONTHS = ['ม.ค.', 'ก.พ.', 'มี.ค.', 'เม.ย.', 'พ.ค.', 'มิ.ย.', 'ก.ค.', 'ส.ค.', 'ก.ย.', 'ต.ค.', 'พ.ย.', 'ธ.ค.']
const pad = (n: number) => String(n).padStart(2, '0')

/** ตัวอย่างสด — ติดเวลาเองทุกวินาทีโดยไม่ดัน state ขึ้นไปข้างบน */
function Preview({ o }: { o: OverlayConfig }) {
  const now = useNow(1000)
  const d = new Date(now)
  const h = o.use24h ? d.getHours() : d.getHours() % 12 || 12
  const time = `${pad(h)}:${pad(d.getMinutes())}${o.showSeconds ? `:${pad(d.getSeconds())}` : ''}${o.use24h ? '' : d.getHours() < 12 ? ' AM' : ' PM'}`
  const date = `${d.getDate()} ${THAI_MONTHS[d.getMonth()]} ${d.getFullYear() + 543}`
  const left = o.flashEndsAt ? Math.max(0, Math.round((o.flashEndsAt - now) / 1000)) : 0
  const bg = o.background === 'transparent' ? 'bg-surface-2' : o.background === 'glass-dark' ? 'bg-black/70' : 'bg-white/70'
  const round = o.clockMode === 'wall' || o.clockMode === 'classic' || o.clockMode === 'twinbell'
  return (
    <div className="grid place-items-center rounded-card bg-surface-3 p-4">
      <div
        className={cn('flex w-full max-w-[280px] flex-col items-center gap-2 rounded-card px-4 py-5 text-center', bg, o.shadow && 'shadow-float', o.jitter && 'motion-safe:animate-pulse')}
        style={{ color: o.primaryColor }} aria-label="ตัวอย่าง Overlay" role="img"
      >
        {o.brandText && <span className="text-sm font-semibold" style={{ color: o.accentColor }}>{o.brandText}</span>}
        <div
          className={cn('grid place-items-center', round ? 'size-28 rounded-full border-4' : 'rounded-md border-2 px-4 py-3', o.clockMode === 'led' && 'bg-black', o.glare && 'ring-2 ring-white/40')}
          style={{ borderColor: o.accentColor }}
        >
          <span className={cn('tnum font-semibold', o.clockMode === 'led' || o.clockMode === 'digital' ? 'font-mono text-3xl' : 'text-xl', o.clockMode === 'desk3d' && 'italic')}
            style={o.clockMode === 'led' ? { color: o.accentColor } : undefined}>{time}</span>
        </div>
        {o.showThaiDate && <span className="text-xs opacity-80">{date}</span>}
        {o.subtitleText && <span className="text-xs opacity-80">{o.subtitleText}</span>}
        {o.flashDeal && (
          <div className="mt-1 w-full rounded-ctl px-3 py-1.5 text-sm font-semibold" style={{ background: o.accentColor, color: o.primaryColor }}>
            {o.flashTitle || 'นาทีทอง'} · <span className="tnum">{pad(Math.floor(left / 60))}:{pad(left % 60)}</span>
          </div>
        )}
        {o.ticker && <span className="w-full truncate border-t border-current/20 pt-1.5 text-xs opacity-80">ข้อความวิ่ง · ส่งฟรีทั่วไทย · โปรวันนี้เท่านั้น</span>}
      </div>
    </div>
  )
}

const BACKGROUNDS: { value: OverlayConfig['background']; label: string }[] = [
  { value: 'glass-dark', label: 'กระจกมืด' }, { value: 'glass-light', label: 'สว่าง' }, { value: 'transparent', label: 'โปร่งใส' },
]

/** ตั้งน้อยที่สุด: แบบนาฬิกา พื้นหลัง ข้อความ — ที่เหลือใช้ค่าเริ่มต้น บันทึกทันทีที่เปลี่ยน */
export default function OverlayTab({ instance }: TabProps) {
  const patch = useApp((s) => s.patchInstance)
  const port = useApp((s) => s.settings.overlayPort)
  const o = instance.overlay
  const set = (p: Partial<OverlayConfig>) => patch(instance.id, { overlay: { ...o, ...p } })
  const url = `http://127.0.0.1:${port}/overlay/${instance.id}`

  return (
    <div className="flex flex-wrap items-start gap-6">
      <div className="w-full max-w-[320px] flex-[1_1_280px]"><Preview o={o} /></div>
      <div className="flex max-w-[520px] flex-[1_1_320px] flex-col gap-5">
        <div className="flex items-start justify-between gap-4">
          <div>
            <h2 className="text-lg font-semibold">นาฬิกาบนไลฟ์</h2>
            <p className="text-sm text-muted">ใส่ลิงก์ครั้งเดียว ใช้ได้ทุกไลฟ์</p>
          </div>
          <Switch checked={o.enabled} onChange={(v) => set({ enabled: v })} label="แสดงนาฬิกาบนไลฟ์" />
        </div>
        <Field label="แบบนาฬิกา">
          <Select value={o.clockMode} onChange={(e) => set({ clockMode: e.target.value as ClockMode })} className="h-11">
            {MODES.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
          </Select>
        </Field>
        <Field label="พื้นหลัง">
          <Segmented label="พื้นหลัง" value={o.background} onChange={(v) => set({ background: v })} options={BACKGROUNDS} />
        </Field>
        <Field label="ข้อความใต้นาฬิกา">
          <Input value={o.brandText} onChange={(e) => set({ brandText: e.target.value })} className="h-11" />
        </Field>
        <div className="flex flex-col gap-2">
          <div className="flex items-center gap-1.5 rounded-ctl bg-surface-2 py-1 pl-3 pr-1">
            <span className="min-w-0 flex-1 truncate font-mono text-[13px] text-muted" title={url}>{url}</span>
            <CopyButton text={url} label="คัดลอกลิงก์" />
          </div>
          <p className="text-[13px] text-muted">ใน TikTok LIVE Studio กด เพิ่มแหล่งที่มา › ลิงก์ แล้ววาง</p>
        </div>
      </div>
    </div>
  )
}
