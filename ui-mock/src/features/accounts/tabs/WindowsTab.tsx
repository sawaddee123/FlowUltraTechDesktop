import { memo, useState } from 'react'
import { AppWindow, ArrowUpToLine, Eye, EyeOff, RefreshCw } from 'lucide-react'
import type { WindowHandle } from '@/lib/types'
import { seedHandles } from '@/lib/mock'
import { useApp } from '@/lib/store'
import { cn } from '@/lib/cn'
import { Badge, Button, EmptyState, Skeleton, Switch } from '@/components/ui/base'
import { DataTable, tdc } from '@/components/ui/layout'
import type { TabProps } from './registry'
import { NeedRunning } from './tools/NeedRunning'
import { useFakeLoad, useTimers } from './tools/hooks'

const HEAD = [
  { label: 'แสดง', className: 'w-[72px]' }, { label: 'HWND', className: 'w-[120px]' }, { label: 'บทบาท', className: 'w-[130px]' },
  { label: 'ขนาด', className: 'w-[96px]' }, { label: 'Title' }, { label: '', className: 'w-[148px]' },
]

const Row = memo(function Row({ h, disabled, onToggle, onRaise }: {
  h: WindowHandle; disabled: boolean; onToggle: (hwnd: string, v: boolean) => void; onRaise: (h: WindowHandle) => void
}) {
  return (
    <tr className="transition-colors hover:bg-surface-2/60">
      <td className={tdc}><Switch size="sm" checked={h.visible} disabled={disabled} label={`แสดงหน้าต่าง ${h.hwnd}`} onChange={(v) => onToggle(h.hwnd, v)} /></td>
      <td className={cn(tdc, 'font-mono text-[13px]')}>{h.hwnd}</td>
      <td className={tdc}><span className={cn('text-sm', h.role === 'Main Studio' && 'font-medium')}>{h.role}</span></td>
      <td className={cn(tdc, 'text-sm text-muted tnum')}>{h.size}</td>
      <td className={cn(tdc, 'max-w-0')}><span className="block truncate text-sm text-muted" title={h.title}>{h.title}</span></td>
      <td className={cn(tdc, 'text-right')}>
        <Button size="sm" disabled={disabled} icon={<ArrowUpToLine className="size-3.5" />} onClick={() => onRaise(h)}>นำขึ้นหน้าสุด</Button>
      </td>
    </tr>
  )
})

function TableSkeleton() {
  return (
    <div className="flex flex-col gap-3 px-4 py-3" aria-busy="true" aria-label="กำลังโหลดหน้าต่าง">
      {Array.from({ length: 4 }, (_, i) => <Skeleton key={i} className="h-9 w-full" />)}
    </div>
  )
}

export default function WindowsTab({ instance: i }: TabProps) {
  const setHidden = useApp((s) => s.setWindowHidden)
  const toast = useApp((s) => s.toast)
  const later = useTimers()
  const running = i.status === 'running'
  const first = useFakeLoad()
  const [refreshing, setRefreshing] = useState(false)
  const [handles, setHandles] = useState<WindowHandle[]>(() => seedHandles())

  const isMain = (h: WindowHandle) => h.role === 'Main Studio'
  // หน้าต่างหลักผูกกับ instance.windowHidden ส่วนหน้าต่างอื่นเป็นสถานะท้องถิ่น
  const rows = handles.map((h) => (isMain(h) ? { ...h, visible: !i.windowHidden } : h))
  const visibleCount = rows.filter((h) => h.visible).length

  const toggle = (hwnd: string, v: boolean) => {
    const h = handles.find((x) => x.hwnd === hwnd)
    if (h && isMain(h)) setHidden(i.id, !v)
    else setHandles((cur) => cur.map((x) => (x.hwnd === hwnd ? { ...x, visible: v } : x)))
  }
  const setAll = (v: boolean) => {
    setHandles((cur) => cur.map((x) => ({ ...x, visible: v })))
    setHidden(i.id, !v)
    toast({ tone: 'info', title: v ? 'แสดงทุกหน้าต่างแล้ว' : 'ซ่อนทุกหน้าต่างแล้ว' })
  }
  const refresh = () => {
    setRefreshing(true)
    later(() => { setRefreshing(false); toast({ tone: 'ok', title: 'รีเฟรชหน้าต่างแล้ว', detail: `พบ ${handles.length} หน้าต่าง` }) }, 700)
  }
  const raise = (h: WindowHandle) => toast({ tone: 'ok', title: 'นำขึ้นหน้าสุดแล้ว', detail: `${h.hwnd} · ${h.role}` })

  return (
    <div className="flex h-full min-h-0 flex-col">
      <div className="flex flex-wrap items-center gap-2 px-6 pb-3 pt-4">
        <Button size="sm" className="min-w-24" loading={refreshing} disabled={!running} icon={<RefreshCw className="size-3.5" />} onClick={refresh}>รีเฟรช</Button>
        <Button size="sm" disabled={!running} icon={<Eye className="size-3.5" />} onClick={() => setAll(true)}>แสดงทุกหน้าต่าง</Button>
        <Button size="sm" disabled={!running} icon={<EyeOff className="size-3.5" />} onClick={() => setAll(false)}>ซ่อนทุกหน้าต่าง</Button>
        {running && <span className="ml-auto text-xs text-subtle tnum">แสดง {visibleCount}/{rows.length} หน้าต่าง</span>}
      </div>

      <div className="flex shrink-0 flex-col gap-3 px-6 pb-3">
        {!running && <NeedRunning instance={i} text="ต้องเริ่มบัญชีก่อนจึงควบคุมหน้าต่างได้" />}
        <div className="flex items-center gap-3 rounded-ctl bg-surface-2/60 px-3.5 py-2.5">
          {i.windowHidden ? <EyeOff className="size-4 shrink-0 text-subtle" aria-hidden /> : <Eye className="size-4 shrink-0 text-subtle" aria-hidden />}
          <div className="min-w-0 flex-1">
            <p className="text-sm font-medium">หน้าต่างหลัก{i.windowHidden ? 'ถูกซ่อนด้วย SW_HIDE' : 'แสดงอยู่ตามปกติ'}</p>
            <p className="text-xs text-subtle">{i.windowHidden ? 'ลดภาระ GPU และไม่รบกวนหน้าจอ บอทยังทำงานต่อ' : 'ซ่อนได้เมื่อไม่ต้องดู Studio'}</p>
          </div>
          <Badge tone={i.windowHidden ? 'info' : 'neutral'}>{i.windowHidden ? 'ซ่อน' : 'แสดง'}</Badge>
          <Switch checked={i.windowHidden} disabled={!running} label="ซ่อนหน้าต่างหลัก (SW_HIDE)" onChange={(v) => setHidden(i.id, v)} />
        </div>
      </div>

      <div className="scroll-y min-h-0 flex-1">
        {!first ? <TableSkeleton /> : !running ? (
          <EmptyState icon={<AppWindow />} title="ไม่มีหน้าต่างให้ควบคุม" hint="Handles จะปรากฏเมื่อ Studio ของบัญชีนี้ทำงานอยู่" />
        ) : (
          <div className="overflow-x-auto">
            <DataTable className="min-w-[760px] table-fixed" head={HEAD}>
              {rows.map((h) => <Row key={h.hwnd} h={h} disabled={refreshing} onToggle={toggle} onRaise={raise} />)}
            </DataTable>
          </div>
        )}
      </div>
    </div>
  )
}
