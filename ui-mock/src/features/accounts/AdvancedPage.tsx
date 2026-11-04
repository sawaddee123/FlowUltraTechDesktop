import { Suspense } from 'react'
import { useNavigate } from 'react-router-dom'
import type { Instance } from '@/lib/types'
import { displayName } from '@/lib/format'
import { Tabs } from '@/components/ui/layout'
import { advancedComponents, advancedTabs, isAdvancedId, type AdvancedId } from './tabs/registry'
import { AccountHeader, TabFallback } from './AccountDetail'

/** เครื่องมือแก้ปัญหา — แยกออกจากหน้าบัญชีเพื่อให้การใช้งานปกติเหลือแค่ที่จำเป็น */
export function AdvancedPage({ instance: i, sub }: { instance: Instance; sub?: string }) {
  const nav = useNavigate()
  const active: AdvancedId = isAdvancedId(sub) ? sub : 'session'
  const Body = advancedComponents[active]
  return (
    <div className="flex h-full min-h-[640px] flex-col gap-5 px-10 pb-8 pt-6 max-[900px]:px-5">
      <AccountHeader instance={i} back={{ to: `/accounts/${i.id}`, label: i.loggedIn ? displayName(i) : i.name }} />
      <div>
        <h2 className="text-lg font-semibold">ขั้นสูง</h2>
        <p className="text-sm text-muted">ไม่จำเป็นต่อการใช้งานปกติ ใช้ตอนแก้ปัญหา</p>
      </div>
      <div className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-card bg-surface">
        <Tabs label="หมวดขั้นสูง" value={active} onChange={(t) => nav(`/accounts/${i.id}/advanced/${t}`)} className="px-4 shadow-[inset_0_-1px_0_var(--line)]" tabs={advancedTabs} />
        <div className="min-h-0 flex-1 overflow-hidden">
          <Suspense fallback={<div className="px-6"><TabFallback /></div>}><Body key={`${i.id}-${active}`} instance={i} /></Suspense>
        </div>
      </div>
    </div>
  )
}
