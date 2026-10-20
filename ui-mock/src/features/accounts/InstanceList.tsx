import { useMemo, useState } from 'react'
import { LayoutDashboard, Plus, UserX } from 'lucide-react'
import { useApp } from '@/lib/store'
import { cn } from '@/lib/cn'
import { Button, EmptyState, Segmented } from '@/components/ui/base'
import { InstanceRow } from './InstanceRow'

type Filter = 'all' | 'running' | 'attention'

export function InstanceList({ activeId, onSelect, onOverview }: { activeId?: string; onSelect: (id: string) => void; onOverview: () => void }) {
  const instances = useApp((s) => s.instances)
  const addInstance = useApp((s) => s.addInstance)
  const [filter, setFilter] = useState<Filter>('all')

  const list = useMemo(() => instances.filter((i) =>
    filter === 'all' ? true : filter === 'running' ? i.status === 'running' || i.status === 'starting' : !!i.captcha || i.status === 'error' || !i.loggedIn,
  ), [instances, filter])

  return (
    <section aria-label="รายการบัญชี" className="flex h-full w-[330px] shrink-0 flex-col rounded-card bg-surface max-[1180px]:w-[290px]">
      <div className="flex items-center justify-between gap-2 px-3 pb-2 pt-3">
        <Segmented
          size="sm" label="กรองบัญชี" value={filter} onChange={setFilter}
          options={[{ value: 'all', label: 'ทั้งหมด' }, { value: 'running', label: 'ทำงาน' }, { value: 'attention', label: 'ต้องดู' }]}
        />
        <Button size="sm" variant="ghost" icon={<Plus className="size-4" />} onClick={() => { const id = addInstance(); if (id) onSelect(id) }}>เพิ่ม</Button>
      </div>

      <button
        type="button" onClick={onOverview} aria-current={!activeId ? 'true' : undefined}
        className={cn('mx-2 mb-1 flex h-9 items-center gap-2.5 rounded-ctl px-3 text-sm font-medium transition-colors', !activeId ? 'bg-surface-2 text-fg' : 'text-muted hover:bg-surface-2/60')}
      >
        <LayoutDashboard className="size-4 text-subtle" aria-hidden />ภาพรวมทุกบัญชี
      </button>

      <div className="scroll-y min-h-0 flex-1 px-2 pb-2">
        {list.length === 0
          ? <EmptyState icon={<UserX />} title="ไม่มีบัญชีตามตัวกรอง" action={<Button size="sm" onClick={() => setFilter('all')}>ดูทั้งหมด</Button>} />
          : <ul className="flex flex-col gap-0.5">{list.map((i) => <li key={i.id}><InstanceRow inst={i} active={i.id === activeId} onSelect={onSelect} /></li>)}</ul>}
      </div>
    </section>
  )
}
