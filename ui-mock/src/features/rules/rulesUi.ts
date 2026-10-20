import { Gift, Heart, MessageSquareText, UserPlus, type LucideIcon } from 'lucide-react'
import type { AutomationRule, RuleTrigger } from '@/lib/types'

export const triggerMeta: Record<RuleTrigger, { label: string; icon: LucideIcon; vars: string[] }> = {
  ChatKeyword: { label: 'คำในแชท', icon: MessageSquareText, vars: ['User', 'Nickname', 'Comment'] },
  GiftMinDiamonds: { label: 'ของขวัญ', icon: Gift, vars: ['User', 'Nickname', 'GiftName', 'Diamonds', 'Count'] },
  MemberJoin: { label: 'คนเข้าห้อง', icon: UserPlus, vars: ['User', 'Nickname'] },
  LikeMilestone: { label: 'ไลก์ถึงเป้า', icon: Heart, vars: ['User', 'Nickname', 'Likes', 'Count'] },
}
export const triggers = Object.keys(triggerMeta) as RuleTrigger[]

/** เกณฑ์ไลก์: Pattern ที่เป็นตัวเลขใช้แทน MinDiamonds (ตามโค้ด RuleEngine) */
export const likeThreshold = (r: AutomationRule) => (r.pattern.trim() !== '' && Number.isFinite(Number(r.pattern)) ? Number(r.pattern) : r.minDiamonds)

/** Regex เสียจะถูกข้ามเงียบๆ ตอนทำงานจริง — คืนข้อความผิดพลาดสั้นๆ เพื่อเตือนบนช่อง pattern */
export function regexError(r: Pick<AutomationRule, 'trigger' | 'isRegex' | 'pattern'>): string | null {
  if (r.trigger !== 'ChatKeyword' || !r.isRegex || !r.pattern) return null
  try { new RegExp(r.pattern, 'i'); return null } catch (e) {
    return (e as Error).message.replace(/^Invalid regular expression: /, '').replace(/^\/.*\/[a-z]*: /, '')
  }
}

export function summary(r: AutomationRule): string {
  switch (r.trigger) {
    case 'ChatKeyword': return r.pattern ? (r.isRegex ? `/${r.pattern}/` : r.pattern) : 'ยังไม่ตั้งคำ'
    case 'GiftMinDiamonds': return `เพชร ≥ ${r.minDiamonds.toLocaleString('en-US')}`
    case 'MemberJoin': return 'ทุกคนที่เข้าห้อง'
    case 'LikeMilestone': return `ไลก์ ≥ ${likeThreshold(r).toLocaleString('en-US')}`
  }
}

/** แทน {ชื่อ} แบบไม่สนตัวพิมพ์ — ตัวแปรที่ไม่รู้จักคงไว้ตามเดิม */
export function fill(tpl: string, vars: Record<string, string | number>): string {
  const low: Record<string, string> = {}
  for (const k of Object.keys(vars)) low[k.toLowerCase()] = String(vars[k])
  return tpl.replace(/\{(\w+)\}/g, (m, k: string) => low[k.toLowerCase()] ?? m)
}

export function unknownVars(r: AutomationRule): string[] {
  const ok = triggerMeta[r.trigger].vars.map((v) => v.toLowerCase())
  const found = [...r.reply.matchAll(/\{(\w+)\}/g)].map((m) => m[1])
  return [...new Set(found.filter((v) => !ok.includes(v.toLowerCase())))]
}

/* ───────── ตัวทดสอบ ───────── */
export const sampleUser = { nickname: 'น้องมิ้นท์', username: 'mint.th' }
export const giftOptions: [string, number][] = [['Rose', 1], ['Finger Heart', 5], ['Doughnut', 30], ['Galaxy', 1000], ['Lion', 29999]]

export type Sample =
  | { kind: 'chat'; comment: string }
  | { kind: 'gift'; gift: string; count: number }
  | { kind: 'join' }
  | { kind: 'like'; likes: number }

export interface Verdict {
  rule: AutomationRule
  state: 'fire' | 'miss' | 'off' | 'badregex' | 'shadowed'
  reply?: string
  note?: string
}

const kindTrigger: Record<Sample['kind'], RuleTrigger> = { chat: 'ChatKeyword', gift: 'GiftMinDiamonds', join: 'MemberJoin', like: 'LikeMilestone' }

export function evaluate(rules: AutomationRule[], s: Sample): Verdict[] {
  const list = rules.filter((r) => r.trigger === kindTrigger[s.kind])
  const base = { User: sampleUser.username, Nickname: sampleUser.nickname }
  const diamonds = s.kind === 'gift' ? (giftOptions.find((g) => g[0] === s.gift)?.[1] ?? 0) * Math.max(1, s.count) : 0
  let giftWinner: string | null = null
  if (s.kind === 'gift') {
    const pass = list.filter((r) => r.enabled && diamonds >= r.minDiamonds).sort((a, b) => b.minDiamonds - a.minDiamonds)
    giftWinner = pass[0]?.id ?? null
  }

  return list.map((rule): Verdict => {
    if (!rule.enabled) return { rule, state: 'off', note: 'ปิดอยู่' }
    switch (s.kind) {
      case 'chat': {
        if (!rule.pattern) return { rule, state: 'miss', note: 'ยังไม่ตั้งคำ' }
        if (rule.isRegex) {
          let re: RegExp
          try { re = new RegExp(rule.pattern, 'i') } catch { return { rule, state: 'badregex', note: 'Regex ผิด · ถูกข้าม' } }
          if (!re.test(s.comment)) return { rule, state: 'miss', note: 'ไม่ตรง' }
        } else if (!s.comment.toLowerCase().includes(rule.pattern.toLowerCase())) return { rule, state: 'miss', note: 'ไม่พบคำ' }
        return { rule, state: 'fire', reply: fill(rule.reply, { ...base, Comment: s.comment }) }
      }
      case 'gift': {
        if (diamonds < rule.minDiamonds) return { rule, state: 'miss', note: `ต้อง ≥ ${rule.minDiamonds.toLocaleString('en-US')} เพชร (ส่ง ${diamonds.toLocaleString('en-US')})` }
        if (rule.id !== giftWinner) return { rule, state: 'shadowed', note: 'ข้าม · กฎเกณฑ์สูงกว่าทำงานก่อน' }
        return { rule, state: 'fire', reply: fill(rule.reply, { ...base, GiftName: s.gift, Diamonds: diamonds, Count: s.count }) }
      }
      case 'join': return { rule, state: 'fire', reply: fill(rule.reply, base) }
      case 'like': {
        const thr = likeThreshold(rule)
        if (s.likes < thr) return { rule, state: 'miss', note: `ต้อง ≥ ${thr.toLocaleString('en-US')} ไลก์` }
        return { rule, state: 'fire', reply: fill(rule.reply, { ...base, Likes: s.likes, Count: s.likes }) }
      }
    }
  })
}

export const templates: { id: string; label: string; sub: string; patch: Partial<AutomationRule> }[] = [
  { id: 'price', label: 'ตอบเมื่อถามราคา', sub: 'คำในแชท · Regex', patch: { name: 'ถามราคา', trigger: 'ChatKeyword', pattern: 'ราคา|เท่าไหร่', isRegex: true, reply: '@{Nickname} ราคาอยู่ในตะกร้าเหลืองค่ะ', cooldownSeconds: 5 } },
  { id: 'gift', label: 'ขอบคุณของขวัญ', sub: 'ของขวัญ ≥ 100 เพชร', patch: { name: 'ขอบคุณของขวัญ', trigger: 'GiftMinDiamonds', minDiamonds: 100, reply: 'ขอบคุณ {Nickname} สำหรับ {GiftName} x{Count} ค่ะ', cooldownSeconds: 3 } },
  { id: 'join', label: 'ทักทายคนเข้าห้อง', sub: 'คนเข้าห้อง', patch: { name: 'ทักทายคนเข้าห้อง', trigger: 'MemberJoin', reply: 'ยินดีต้อนรับคุณ {Nickname} ค่ะ', cooldownSeconds: 10 } },
]
