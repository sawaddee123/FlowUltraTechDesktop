import { useMemo, useState } from 'react'
import { Plus, Trash2, Workflow } from 'lucide-react'
import { useShallow } from 'zustand/react/shallow'
import { useApp } from '@/lib/store'
import type { AutomationRule, RuleTrigger } from '@/lib/types'
import { Badge, Button, EmptyState, Skeleton, StatusDot, Switch } from '@/components/ui/base'
import { Field, Input, NumberInput, Select, Textarea, ToggleRow } from '@/components/ui/form'
import { Confirm } from '@/components/ui/overlay'
import { PageHeader, Section } from '@/components/ui/layout'
import { cn } from '@/lib/cn'
import { useFirstLoad } from '../_pages/hooks'

const triggerLabel: Record<RuleTrigger, string> = {
  ChatKeyword: 'คำในแชท', GiftMinDiamonds: 'ของขวัญขั้นต่ำ', MemberJoin: 'คนเข้าห้อง', LikeMilestone: 'ไลก์ครบเป้า',
}

function regexError(p: string): string | null {
  try { new RegExp(p, 'i'); return null } catch (e) { return e instanceof Error ? e.message : 'รูปแบบไม่ถูกต้อง' }
}

function Tester({ rule }: { rule: AutomationRule }) {
  const [text, setText] = useState('ราคาเท่าไหร่คะ')
  const [diamonds, setDiamonds] = useState(1000)
  const [likes, setLikes] = useState(10000)
  const nick = 'มะลิ'
  const result = useMemo(() => {
    let match = false
    if (rule.trigger === 'ChatKeyword') {
      if (!rule.pattern) match = false
      else if (rule.isRegex) { match = !regexError(rule.pattern) && new RegExp(rule.pattern, 'i').test(text) }
      else match = text.toLowerCase().includes(rule.pattern.toLowerCase())
    } else if (rule.trigger === 'GiftMinDiamonds') match = diamonds >= rule.minDiamonds
    else if (rule.trigger === 'MemberJoin') match = true
    else match = likes >= (Number(rule.pattern) || 0)
    const reply = rule.reply
      .replace(/\{nickname\}/gi, nick).replace(/\{giftname\}/gi, 'Rose').replace(/\{count\}/gi, '3').replace(/\{likes\}/gi, likes.toLocaleString('en-US'))
    return { match, reply }
  }, [rule, text, diamonds, likes])

  return (
    <Section title="ทดสอบกฎ">
      <div className="flex flex-col gap-3 rounded-card bg-surface-2 p-4">
        {rule.trigger === 'ChatKeyword' && <Field label="ข้อความแชทตัวอย่าง"><Input value={text} onChange={(e) => setText(e.target.value)} /></Field>}
        {rule.trigger === 'GiftMinDiamonds' && <Field label="เพชรของขวัญตัวอย่าง"><NumberInput value={diamonds} min={0} onChange={setDiamonds} /></Field>}
        {rule.trigger === 'LikeMilestone' && <Field label="จำนวนไลก์ตัวอย่าง"><NumberInput value={likes} min={0} onChange={setLikes} /></Field>}
        {rule.trigger === 'MemberJoin' && <p className="text-sm text-muted">จำลองคนชื่อ {nick} เข้าห้อง</p>}
        <div aria-live="polite" className="flex flex-col gap-1.5">
          <Badge tone={result.match ? 'ok' : 'neutral'} dot className="self-start">{result.match ? 'ตรงเงื่อนไข' : 'ไม่ตรงเงื่อนไข'}</Badge>
          {result.match && <p className="rounded-ctl bg-surface px-3 py-2 text-sm">{result.reply || <span className="text-subtle">ยังไม่ได้ใส่ข้อความตอบ</span>}</p>}
        </div>
      </div>
    </Section>
  )
}

function RuleEditor({ rule }: { rule: AutomationRule }) {
  const { patchRule, removeRule, toast } = useApp(useShallow((s) => ({ patchRule: s.patchRule, removeRule: s.removeRule, toast: s.toast })))
  const [del, setDel] = useState(false)
  const set = (p: Partial<AutomationRule>) => patchRule(rule.id, p)
  const err = rule.trigger === 'ChatKeyword' && rule.isRegex && rule.pattern ? regexError(rule.pattern) : null
  return (
    <div className="scroll-y flex h-full min-w-0 flex-1 flex-col rounded-card bg-surface px-6 pb-6">
      <div className="flex items-center justify-between gap-3 pb-1 pt-5">
        <div className="min-w-0">
          <h2 className="truncate text-lg font-semibold">{rule.name || 'กฎไม่มีชื่อ'}</h2>
          <p className="text-sm text-subtle tnum">ทำงานแล้ว {rule.hits.toLocaleString('en-US')} ครั้ง</p>
        </div>
        <div className="flex items-center gap-3">
          <Switch checked={rule.enabled} onChange={(v) => set({ enabled: v })} label={`เปิดใช้กฎ ${rule.name}`} />
          <Button variant="danger" size="sm" icon={<Trash2 className="size-4" />} onClick={() => setDel(true)}>ลบ</Button>
        </div>
      </div>
      <Section title="เงื่อนไข">
        <div className="grid grid-cols-2 gap-4 max-[1180px]:grid-cols-1">
          <Field label="ชื่อกฎ"><Input value={rule.name} onChange={(e) => set({ name: e.target.value })} /></Field>
          <Field label="ตัวกระตุ้น">
            <Select value={rule.trigger} onChange={(e) => set({ trigger: e.target.value as RuleTrigger })}>
              {(Object.keys(triggerLabel) as RuleTrigger[]).map((t) => <option key={t} value={t}>{triggerLabel[t]}</option>)}
            </Select>
          </Field>
          {rule.trigger === 'ChatKeyword' && (
            <div className="col-span-2 max-[1180px]:col-span-1">
              <Field label="คำที่ตรวจ" hint="โหมดปกติ: ข้อความมีคำนี้อยู่ · โหมด Regex: ใช้รูปแบบ เช่น ราคา|กี่บาท" error={err ?? undefined}>
                <Input mono aria-invalid={!!err} value={rule.pattern} onChange={(e) => set({ pattern: e.target.value })} placeholder="ราคา|เท่าไหร่" />
              </Field>
              <ToggleRow title="ใช้ Regex" sub={rule.isRegex ? (rule.pattern ? (err ? 'รูปแบบไม่ถูกต้อง' : 'รูปแบบถูกต้อง') : 'ใส่รูปแบบเพื่อตรวจ') : 'ปิดอยู่ ตรวจแบบมีคำนี้อยู่'}>
                <Switch checked={rule.isRegex} onChange={(v) => set({ isRegex: v })} label="ใช้ Regex" />
              </ToggleRow>
            </div>
          )}
          {rule.trigger === 'GiftMinDiamonds' && <Field label="เพชรขั้นต่ำ"><NumberInput value={rule.minDiamonds} min={0} step={10} suffix="เพชร" onChange={(n) => set({ minDiamonds: n })} /></Field>}
          {rule.trigger === 'LikeMilestone' && <Field label="ไลก์เป้าหมาย"><NumberInput value={Number(rule.pattern) || 0} min={0} step={1000} onChange={(n) => set({ pattern: String(n) })} /></Field>}
          <Field label="คูลดาวน์" hint="รอกี่วินาทีก่อนกฎเดิมทำงานซ้ำได้"><NumberInput value={rule.cooldownSeconds} min={0} suffix="วิ." onChange={(n) => set({ cooldownSeconds: n })} /></Field>
        </div>
      </Section>
      <Section title="ข้อความตอบ">
        <Field label="ข้อความ" hint="ตัวแปร: {Nickname} {GiftName} {Count} {Likes}">
          <Textarea value={rule.reply} onChange={(e) => set({ reply: e.target.value })} placeholder="@{Nickname} ขอบคุณค่ะ" />
        </Field>
        <p className="mt-1.5 text-xs text-subtle">ใส่ตัวแปรเช่น {'{Nickname}'} เพื่อเรียกชื่อคนส่ง</p>
      </Section>
      <Tester rule={rule} />
      <Confirm
        open={del} title={`ลบกฎ ${rule.name || 'นี้'}?`} detail="ลบแล้วกู้คืนไม่ได้" confirmLabel="ลบกฎ"
        onCancel={() => setDel(false)} onConfirm={() => { setDel(false); removeRule(rule.id); toast({ tone: 'info', title: 'ลบกฎแล้ว' }) }}
      />
    </div>
  )
}

export default function RulesPage() {
  const loading = useFirstLoad('rules')
  const { rules, addRule, patchRule } = useApp(useShallow((s) => ({ rules: s.rules, addRule: s.addRule, patchRule: s.patchRule })))
  const [selId, setSelId] = useState<string | null>(null)
  const current = rules.find((r) => r.id === selId) ?? rules[0]
  const add = () => setSelId(addRule())

  return (
    <div className="flex h-full flex-col">
      <PageHeader title="กฎอัตโนมัติ" sub="ตอบแชท ของขวัญ และคนเข้าห้องตามเงื่อนไข" actions={<Button variant="primary" icon={<Plus className="size-4" />} onClick={add}>เพิ่มกฎ</Button>} />
      {loading ? (
        <div className="flex min-h-0 flex-1 gap-3 pb-1" aria-busy="true" aria-label="กำลังโหลด">
          <div className="flex w-[340px] shrink-0 flex-col gap-2 rounded-card bg-surface p-3"><Skeleton className="h-14 w-full" /><Skeleton className="h-14 w-full" /><Skeleton className="h-14 w-full" /></div>
          <div className="flex flex-1 flex-col gap-4 rounded-card bg-surface p-6"><Skeleton className="h-10 w-1/2" /><Skeleton className="h-40 w-full" /><Skeleton className="h-32 w-full" /></div>
        </div>
      ) : rules.length === 0 ? (
        <div className="min-h-0 flex-1 pb-1 pt-1">
          <EmptyState icon={<Workflow />} title="ยังไม่มีกฎ" hint="สร้างกฎแรก เช่น ตอบอัตโนมัติเมื่อมีคนถามราคา แล้วทดสอบก่อนใช้จริง" action={<Button variant="primary" icon={<Plus className="size-4" />} onClick={add}>เพิ่มกฎ</Button>} />
        </div>
      ) : (
        <div className="flex min-h-0 flex-1 gap-3 pb-1">
          <section aria-label="รายการกฎ" className="scroll-y w-[340px] shrink-0 rounded-card bg-surface p-2 max-[1180px]:w-[300px]">
            <ul className="flex flex-col gap-0.5">
              {rules.map((r) => (
                <li key={r.id} className={cn('flex items-center gap-2 rounded-ctl pr-3 transition-colors', r.id === current?.id ? 'bg-primary-soft' : 'hover:bg-surface-2')}>
                  <button type="button" aria-current={r.id === current?.id} onClick={() => setSelId(r.id)} className="flex min-w-0 flex-1 items-center gap-3 px-3 py-2.5 text-left">
                    <StatusDot tone={r.enabled ? 'ok' : 'neutral'} />
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-sm font-medium">{r.name || 'กฎไม่มีชื่อ'}</span>
                      <span className="block truncate text-xs text-subtle">{triggerLabel[r.trigger]} · {r.hits} ครั้ง</span>
                    </span>
                  </button>
                  <Switch size="sm" checked={r.enabled} onChange={(v) => patchRule(r.id, { enabled: v })} label={`เปิดใช้กฎ ${r.name}`} />
                </li>
              ))}
            </ul>
          </section>
          {current && <RuleEditor key={current.id} rule={current} />}
        </div>
      )}
    </div>
  )
}
