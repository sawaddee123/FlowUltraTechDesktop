import { useNavigate, useParams } from 'react-router-dom'
import { ChevronDown, EyeOff, Eye, Play, ScanSearch, Square } from 'lucide-react'
import { useApp } from '@/lib/store'
import { Button } from '@/components/ui/base'
import { Menu } from '@/components/ui/overlay'
import { PageHeader } from '@/components/ui/layout'
import { InstanceList } from './InstanceList'
import { InstanceDetail } from './InstanceDetail'
import { FleetSummary } from './FleetSummary'

export default function AccountsPage() {
  const { id, tab } = useParams()
  const nav = useNavigate()
  const instances = useApp((s) => s.instances)
  const startAll = useApp((s) => s.startAll)
  const stopAll = useApp((s) => s.stopAll)
  const hideAll = useApp((s) => s.hideAll)
  const scanAll = useApp((s) => s.scanAll)
  const current = instances.find((i) => i.id === id)

  return (
    <div className="flex h-full flex-col">
      <PageHeader
        title="บัญชีสตูดิโอ"
        actions={
          <>
            <Button variant="primary" icon={<Play className="size-3.5 fill-current" />} onClick={startAll}>เริ่มทุกจอ</Button>
            <Button icon={<Square className="size-3.5 fill-current" />} onClick={stopAll}>ปิดทุกจอ</Button>
            <Menu
              width={200}
              items={[
                { label: 'ซ่อนทุกจอ', icon: <EyeOff />, onSelect: () => hideAll(true) },
                { label: 'แสดงทุกจอ', icon: <Eye />, onSelect: () => hideAll(false) },
                { label: 'สแกน Captcha', icon: <ScanSearch />, divider: true, onSelect: scanAll },
              ]}
              trigger={({ toggle }) => <Button variant="ghost" onClick={toggle}>จัดการจอ<ChevronDown className="size-4" aria-hidden /></Button>}
            />
          </>
        }
      />
      <div className="flex min-h-0 flex-1 gap-3 pb-1">
        <InstanceList activeId={current?.id} onSelect={(i) => nav(`/accounts/${i}${tab ? '/' + tab : ''}`)} onOverview={() => nav('/accounts')} />
        <div className="flex min-w-0 flex-1 flex-col rounded-card">
          {current
            ? <InstanceDetail instance={current} tab={tab} onTab={(t) => nav(`/accounts/${current.id}/${t}`)} />
            : <div className="h-full rounded-card bg-surface"><FleetSummary onSelect={(i) => nav(`/accounts/${i}`)} /></div>}
        </div>
      </div>
    </div>
  )
}
