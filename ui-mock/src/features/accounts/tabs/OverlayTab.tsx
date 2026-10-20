import { useState } from 'react'
import { Play, Square } from 'lucide-react'
import type { ClockMode, OverlayConfig } from '@/lib/types'
import { useApp } from '@/lib/store'
import { useNow } from '@/lib/format'
import { cn } from '@/lib/cn'
import { Button, CopyButton, Segmented, Switch } from '@/components/ui/base'
import { Field, Input, NumberInput, Select, ToggleRow, ColorInput } from '@/components/ui/form'
import { Section } from '@/components/ui/layout'
import type { TabProps } from './registry'

const MODES: { value: ClockMode; label: string }[] = [
  { value: 'wall', label: 'นาฬิกาแขวน' }, { value: 'classic', label: 'คลาสสิก' }, { value: 'desk3d', label: 'ตั้งโต๊ะ 3D' },
  { value: 'twinbell', label: 'ระฆังคู่' }, { value: 'led', label: 'LED' }, { value: 'digital', label: 'ดิจิทัล' },
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

export default function OverlayTab({ instance }: TabProps) {
  const patch = useApp((s) => s.patchInstance)
  const toast = useApp((s) => s.toast)
  const port = useApp((s) => s.settings.overlayPort)
  const [draft, setDraft] = useState<OverlayConfig>(instance.overlay)
  const dirty = JSON.stringify(draft) !== JSON.stringify(instance.overlay)
  const set = (p: Partial<OverlayConfig>) => setDraft((c) => ({ ...c, ...p }))
  const url = `http://127.0.0.1:${port}/overlay/${instance.id}`
  const counting = !!draft.flashEndsAt && draft.flashEndsAt > Date.now()

  const save = () => {
    patch(instance.id, { overlay: draft })
    toast({ tone: 'ok', title: 'บันทึก Overlay แล้ว' })
  }

  return (
    <div className="relative flex h-full min-h-0 flex-col">
      <div className="grid min-h-0 flex-1 gap-6 overflow-y-auto px-6 py-4 lg:grid-cols-[minmax(0,1fr)_300px]">
        <div className="flex min-w-0 flex-col gap-6">
          <Section title="ทั่วไป">
            <ToggleRow title="เปิดใช้ Overlay" sub="แสดงนาฬิกาและนาทีทองบนสตรีม">
              <Switch checked={draft.enabled} onChange={(v) => set({ enabled: v })} label="เปิดใช้ Overlay" />
            </ToggleRow>
            <div className="grid gap-3 sm:grid-cols-2">
              <Field label="รูปแบบนาฬิกา">
                <Select value={draft.clockMode} onChange={(e) => set({ clockMode: e.target.value as ClockMode })} className="h-9">
                  {MODES.map((m) => <option key={m.value} value={m.value}>{m.label}</option>)}
                </Select>
              </Field>
              <Field label="พื้นหลัง">
                <Select value={draft.background} onChange={(e) => set({ background: e.target.value as OverlayConfig['background'] })} className="h-9">
                  <option value="transparent">โปร่งใส</option><option value="glass-dark">กระจกเข้ม</option><option value="glass-light">กระจกสว่าง</option>
                </Select>
              </Field>
              <Field label="สีหลัก"><ColorInput label="สีหลัก" value={draft.primaryColor} onChange={(v) => set({ primaryColor: v })} /></Field>
              <Field label="สีเน้น"><ColorInput label="สีเน้น" value={draft.accentColor} onChange={(v) => set({ accentColor: v })} /></Field>
              <Field label="ชื่อแบรนด์"><Input value={draft.brandText} onChange={(e) => set({ brandText: e.target.value })} className="h-9" /></Field>
              <Field label="ข้อความรอง"><Input value={draft.subtitleText} onChange={(e) => set({ subtitleText: e.target.value })} className="h-9" /></Field>
            </div>
          </Section>

          <Section title="เวลา">
            <div className="mb-1">
              <Segmented label="รูปแบบเวลา" value={draft.use24h ? 24 : 12} onChange={(v) => set({ use24h: v === 24 })}
                options={[{ value: 24, label: '24 ชั่วโมง' }, { value: 12, label: '12 ชั่วโมง' }]} />
            </div>
            <ToggleRow title="แสดงวินาที"><Switch checked={draft.showSeconds} onChange={(v) => set({ showSeconds: v })} label="แสดงวินาที" /></ToggleRow>
            <ToggleRow title="วันที่ไทย" sub="แสดงเป็น พ.ศ."><Switch checked={draft.showThaiDate} onChange={(v) => set({ showThaiDate: v })} label="แสดงวันที่ไทย" /></ToggleRow>
          </Section>

          <Section title="เอฟเฟกต์">
            <ToggleRow title="แสงสะท้อน"><Switch checked={draft.glare} onChange={(v) => set({ glare: v })} label="แสงสะท้อน" /></ToggleRow>
            <ToggleRow title="เงา"><Switch checked={draft.shadow} onChange={(v) => set({ shadow: v })} label="เงา" /></ToggleRow>
            <ToggleRow title="สั่นเล็กน้อย" sub="กันภาพค้างบนจอ"><Switch checked={draft.jitter} onChange={(v) => set({ jitter: v })} label="สั่นเล็กน้อย" /></ToggleRow>
            <ToggleRow title="ข้อความวิ่ง"><Switch checked={draft.ticker} onChange={(v) => set({ ticker: v })} label="ข้อความวิ่ง" /></ToggleRow>
          </Section>

          <Section title="นาทีทอง (Flash deal)">
            <ToggleRow title="เปิดนาทีทอง"><Switch checked={draft.flashDeal} onChange={(v) => set({ flashDeal: v })} label="เปิดนาทีทอง" /></ToggleRow>
            <div className="grid gap-3 sm:grid-cols-[1fr_160px_auto] sm:items-end">
              <Field label="หัวข้อ"><Input value={draft.flashTitle} disabled={!draft.flashDeal} onChange={(e) => set({ flashTitle: e.target.value })} className="h-9" /></Field>
              <Field label="เวลา"><NumberInput value={draft.flashSeconds} min={10} max={3600} suffix="วิ" onChange={(n) => set({ flashSeconds: n })} /></Field>
              {counting ? (
                <Button icon={<Square className="size-3.5 fill-current" />} onClick={() => set({ flashEndsAt: undefined })}>หยุดนับ</Button>
              ) : (
                <Button disabled={!draft.flashDeal} icon={<Play className="size-3.5 fill-current" />}
                  onClick={() => set({ flashEndsAt: Date.now() + draft.flashSeconds * 1000 })}>เริ่มนับถอยหลัง</Button>
              )}
            </div>
          </Section>
        </div>

        <aside className="flex min-w-0 flex-col gap-4 lg:sticky lg:top-0 lg:self-start" aria-label="ตัวอย่างและลิงก์">
          <Preview o={draft} />
          <Field label="URL สำหรับ OBS (Browser Source)">
            <div className="flex items-center gap-1.5 rounded-ctl bg-surface-2 py-1 pl-3 pr-1">
              <span className="min-w-0 flex-1 truncate font-mono text-[13px] text-muted" title={url}>{url}</span>
              <CopyButton text={url} label="คัดลอก URL" />
            </div>
          </Field>
        </aside>
      </div>

      {dirty && (
        <div role="region" aria-label="การเปลี่ยนแปลงที่ยังไม่บันทึก" className="sticky bottom-0 flex shrink-0 items-center justify-between gap-3 bg-surface px-6 py-3 shadow-float">
          <span className="text-sm text-muted">มีการเปลี่ยนแปลงที่ยังไม่บันทึก</span>
          <div className="flex gap-2">
            <Button variant="ghost" onClick={() => setDraft(instance.overlay)}>ยกเลิก</Button>
            <Button variant="primary" onClick={save}>บันทึก</Button>
          </div>
        </div>
      )}
    </div>
  )
}
