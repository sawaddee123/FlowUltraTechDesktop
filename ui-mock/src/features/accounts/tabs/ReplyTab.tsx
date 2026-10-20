import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { Bot, Hand, KeyRound, Repeat, MessageSquareQuote, Sparkles } from 'lucide-react'
import type { AutoReplyConfig } from '@/lib/types'
import { useApp } from '@/lib/store'
import { cn } from '@/lib/cn'
import { Badge, Button, Segmented, Skeleton, Switch } from '@/components/ui/base'
import { Field, Input, NumberInput, SecretInput, Textarea } from '@/components/ui/form'
import type { TabProps } from './registry'
import { useFakeLoad } from './tools/hooks'

type Patch = <K extends keyof AutoReplyConfig>(k: K, v: AutoReplyConfig[K]) => void

function Mode({ icon, title, sub, enabled, onToggle, disabled, children }: {
  icon: ReactNode; title: string; sub: string; enabled: boolean; onToggle: (v: boolean) => void; disabled?: boolean; children: ReactNode
}) {
  return (
    <section className={cn('py-5', disabled && 'opacity-60')}>
      <div className="flex items-center gap-3">
        <span className="grid size-8 shrink-0 place-items-center rounded-ctl bg-surface-2 text-muted [&>svg]:size-4" aria-hidden>{icon}</span>
        <div className="min-w-0 flex-1">
          <h3 className="text-sm font-semibold text-fg">{title}</h3>
          <p className="truncate text-xs text-subtle">{sub}</p>
        </div>
        <Switch checked={enabled} onChange={onToggle} label={`เปิดใช้${title}`} disabled={disabled} />
      </div>
      {enabled && <div className="mt-4 flex flex-col gap-4 pl-11 animate-fade-in">{children}</div>}
    </section>
  )
}

function Advanced({ children, label = 'ตัวเลือกขั้นสูง' }: { children: ReactNode; label?: string }) {
  return (
    <details className="group">
      <summary className="cursor-pointer select-none text-[13px] font-medium text-muted transition-colors hover:text-fg">{label}</summary>
      <div className="mt-3 flex flex-col gap-4">{children}</div>
    </details>
  )
}

const lines = (s: string) => s.split('\n').filter((l) => l.trim()).length

export default function ReplyTab({ instance }: TabProps) {
  const patchInstance = useApp((s) => s.patchInstance)
  const toast = useApp((s) => s.toast)
  const ready = useFakeLoad(300)
  const saved = instance.autoReply
  const [draft, setDraft] = useState<AutoReplyConfig>(saved)
  const [apiKey, setApiKey] = useState('')
  const [editKey, setEditKey] = useState(false)

  useEffect(() => { setDraft(instance.autoReply); setApiKey(''); setEditKey(false) }, [instance.id]) // eslint-disable-line react-hooks/exhaustive-deps

  const set: Patch = (k, v) => setDraft((d) => ({ ...d, [k]: v }))
  const keyChanged = apiKey.trim() !== ''
  const dirty = useMemo(() => keyChanged || JSON.stringify(draft) !== JSON.stringify(saved), [draft, saved, keyChanged])
  const off = !draft.enabled
  const badWords = draft.aiMinWords > draft.aiMaxWords

  const save = () => {
    patchInstance(instance.id, { autoReply: { ...draft, aiKeySet: draft.aiKeySet || keyChanged } })
    setApiKey(''); setEditKey(false)
    toast({ tone: 'ok', title: 'บันทึกการตอบแชทแล้ว', detail: instance.name })
  }
  const cancel = () => { setDraft(saved); setApiKey(''); setEditKey(false) }

  if (!ready) {
    return (
      <div className="flex flex-col gap-4 px-6 py-5" aria-busy="true" aria-label="กำลังโหลดการตั้งค่า">
        {Array.from({ length: 5 }, (_, i) => <Skeleton key={i} className="h-10 w-full" />)}
      </div>
    )
  }

  return (
    <div className="relative flex h-full min-h-0 flex-col">
      <div className="scroll-y min-h-0 flex-1 px-6 pb-6">
        <div className="mx-auto max-w-3xl">
          <div className="flex items-center gap-3 py-5">
            <Bot className="size-5 text-primary" aria-hidden />
            <div className="min-w-0 flex-1">
              <h2 className="text-base font-semibold text-fg">ตอบแชทอัตโนมัติ</h2>
              <p className="text-xs text-subtle">ปิดอยู่ = ทุกโหมดด้านล่างหยุดทำงาน</p>
            </div>
            <Switch checked={draft.enabled} onChange={(v) => set('enabled', v)} label="เปิดตอบแชทอัตโนมัติ" />
          </div>

          <Mode icon={<Repeat />} title="วนส่งข้อความ" sub="ส่งข้อความตามรอบเวลา" enabled={draft.loopEnabled} onToggle={(v) => set('loopEnabled', v)} disabled={off}>
            <Field label="ข้อความ (หนึ่งบรรทัดต่อหนึ่งข้อความ)" hint="ระบบจะวนส่งทีละบรรทัดหรือสุ่มตามที่ตั้งไว้">
              <Textarea value={draft.loopMessages} onChange={(e) => set('loopMessages', e.target.value)} rows={4} placeholder="สนใจสินค้าพิมพ์ 1 ได้เลยค่ะ" />
              <span className="mt-1 block text-xs text-subtle tnum">{lines(draft.loopMessages)} ข้อความ</span>
            </Field>
            <div className="grid grid-cols-2 gap-4">
              <Field label="ส่งทุก"><NumberInput value={draft.loopIntervalSeconds} onChange={(n) => set('loopIntervalSeconds', n)} min={5} max={3600} suffix="วิ" /></Field>
              <Field label="ลำดับ">
                <Segmented size="sm" label="ลำดับการส่ง" value={draft.loopRandom ? 'rand' : 'seq'} onChange={(v) => set('loopRandom', v === 'rand')}
                  options={[{ value: 'seq', label: 'ตามลำดับ' }, { value: 'rand', label: 'สุ่ม' }]} />
              </Field>
            </div>
          </Mode>

          <Mode icon={<MessageSquareQuote />} title="กฎตอบตามคำ" sub="ตอบเมื่อแชทมีคำที่กำหนด" enabled={draft.rulesEnabled} onToggle={(v) => set('rulesEnabled', v)} disabled={off}>
            <Field label="กฎ (คำ=คำตอบ หนึ่งบรรทัดต่อหนึ่งกฎ)" hint="เช่น ราคา=ตอนนี้ลดเหลือ 199 บาทค่ะ">
              <Textarea mono value={draft.rulesText} onChange={(e) => set('rulesText', e.target.value)} rows={5} placeholder="ราคา=199 บาทค่ะ" />
              <span className="mt-1 block text-xs text-subtle tnum">{lines(draft.rulesText)} กฎ</span>
            </Field>
            <Advanced>
              <div className="flex items-center justify-between gap-4">
                <span className="text-sm text-fg">ขึ้นต้นด้วยการแท็กชื่อผู้ถาม</span>
                <Switch size="sm" checked={draft.rulePrefixMention} onChange={(v) => set('rulePrefixMention', v)} label="แท็กชื่อผู้ถาม" />
              </div>
            </Advanced>
          </Mode>

          <Mode icon={<Hand />} title="ทักทายคนเข้าห้อง" sub="ทักผู้ชมที่เพิ่งเข้ามา" enabled={draft.greetEnabled} onToggle={(v) => set('greetEnabled', v)} disabled={off}>
            <Field label="ข้อความทักทาย (หนึ่งบรรทัดต่อหนึ่งข้อความ)">
              <Textarea value={draft.greetMessages} onChange={(e) => set('greetMessages', e.target.value)} rows={3} placeholder="สวัสดีค่ะ ยินดีต้อนรับ" />
            </Field>
            <div className="grid grid-cols-2 gap-4">
              <Field label="ความถี่"><Segmented size="sm" label="ความถี่ทักทาย" value={draft.greetFrequency} onChange={(v) => set('greetFrequency', v)}
                options={[{ value: 'once', label: 'ครั้งเดียว/คน' }, { value: 'every', label: 'ทุกครั้งที่เข้า' }]} /></Field>
              <Field label="รูปแบบ"><Segmented size="sm" label="รูปแบบทักทาย" value={draft.greetMode} onChange={(v) => set('greetMode', v)}
                options={[{ value: 'random', label: 'สุ่มจากรายการ' }, { value: 'ai', label: 'AI' }]} /></Field>
            </div>
            <Advanced>
              <Field label="ห่างกันอย่างน้อย"><NumberInput value={draft.greetMinInterval} onChange={(n) => set('greetMinInterval', n)} min={0} max={600} suffix="วิ" /></Field>
            </Advanced>
          </Mode>

          <Mode icon={<Sparkles />} title="ตอบด้วย AI" sub="ให้โมเดลภาษาตอบแชทแทน" enabled={draft.aiEnabled} onToggle={(v) => set('aiEnabled', v)} disabled={off}>
            <div className="grid grid-cols-2 gap-4">
              <Field label="โหมด"><Segmented size="sm" label="โหมด AI" value={draft.aiMode} onChange={(v) => set('aiMode', v)}
                options={[{ value: 'reply', label: 'ตอบแชท' }, { value: 'periodic', label: 'พูดเป็นช่วง' }]} /></Field>
              <Field label="ตอบเฉพาะ"><Segmented size="sm" label="ตัวกรองแชท" value={draft.aiFilter} onChange={(v) => set('aiFilter', v)}
                options={[{ value: 'all', label: 'ทุกแชท' }, { value: 'questions', label: 'คำถาม' }]} /></Field>
            </div>
            <Field label="คำสั่งระบบ (Prompt)">
              <Textarea value={draft.aiPrompt} onChange={(e) => set('aiPrompt', e.target.value)} rows={3} placeholder="คุณคือแอดมินร้านค้า ตอบสั้น สุภาพ" />
            </Field>
            <Field label="API Key" hint="เก็บในเครื่อง ไม่แสดงค่าเดิมซ้ำ">
              {draft.aiKeySet && !editKey ? (
                <div className="flex h-9 items-center gap-3 rounded-ctl bg-surface-2 px-3">
                  <KeyRound className="size-4 text-subtle" aria-hidden />
                  <Badge tone="ok" dot>ตั้งค่าแล้ว</Badge>
                  <Button size="sm" variant="ghost" className="ml-auto" onClick={() => setEditKey(true)}>เปลี่ยน</Button>
                </div>
              ) : (
                <SecretInput mono value={apiKey} onChange={(e) => setApiKey(e.target.value)} placeholder="sk-…" aria-label="API Key" />
              )}
              {!draft.aiKeySet && !keyChanged && <span className="mt-1 block text-xs text-warn">ยังไม่ได้ตั้งค่า — AI จะไม่ตอบจนกว่าจะใส่คีย์</span>}
            </Field>
            <Advanced>
              <div className="grid grid-cols-2 gap-4">
                <Field label="โมเดล"><Input mono value={draft.aiModel} onChange={(e) => set('aiModel', e.target.value)} /></Field>
                <Field label="Base URL"><Input mono value={draft.aiBaseUrl} onChange={(e) => set('aiBaseUrl', e.target.value)} /></Field>
              </div>
              <Field label="คำที่ห้ามพูด (คั่นด้วยจุลภาค)"><Input value={draft.aiForbidden} onChange={(e) => set('aiForbidden', e.target.value)} /></Field>
              <div className="grid grid-cols-3 gap-4">
                <Field label="คำต่ำสุด"><NumberInput value={draft.aiMinWords} onChange={(n) => set('aiMinWords', n)} min={1} max={200} /></Field>
                <Field label="คำสูงสุด"><NumberInput value={draft.aiMaxWords} onChange={(n) => set('aiMaxWords', n)} min={1} max={400} /></Field>
                <Field label="พักต่อคน"><NumberInput value={draft.aiUserCooldown} onChange={(n) => set('aiUserCooldown', n)} min={0} max={3600} suffix="วิ" /></Field>
              </div>
              {badWords && <p className="text-xs text-bad">คำต่ำสุดต้องไม่เกินคำสูงสุด</p>}
            </Advanced>
          </Mode>

          <section className="py-5">
            <h3 className="mb-3 text-[13px] font-semibold uppercase tracking-wide text-subtle">ตัวจำกัดอัตรา</h3>
            <div className="grid grid-cols-3 gap-4">
              <Field label="ห่างกันอย่างน้อย" hint="เวลาขั้นต่ำระหว่างข้อความที่บอทส่ง"><NumberInput value={draft.minIntervalSeconds} onChange={(n) => set('minIntervalSeconds', n)} min={0} max={600} suffix="วิ" /></Field>
              <Field label="พักต่อคน" hint="ไม่ตอบคนเดิมซ้ำภายในเวลานี้"><NumberInput value={draft.userCooldownSeconds} onChange={(n) => set('userCooldownSeconds', n)} min={0} max={3600} suffix="วิ" /></Field>
              <Field label="สูงสุดต่อนาที"><NumberInput value={draft.maxPerMinute} onChange={(n) => set('maxPerMinute', n)} min={1} max={60} /></Field>
            </div>
          </section>
        </div>
      </div>

      {dirty && (
        <div role="region" aria-label="การเปลี่ยนแปลงที่ยังไม่บันทึก"
          className="sticky bottom-0 z-10 flex items-center gap-3 bg-surface px-6 py-3 shadow-float animate-rise">
          <span className="flex-1 text-sm text-fg">มีการเปลี่ยนแปลงที่ยังไม่บันทึก</span>
          <Button variant="ghost" onClick={cancel}>ยกเลิก</Button>
          <Button variant="primary" disabled={badWords} onClick={save}>บันทึก</Button>
        </div>
      )}
    </div>
  )
}
