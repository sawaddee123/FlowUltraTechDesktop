import { NavLink, useNavigate } from 'react-router-dom'
import { DownloadCloud, FlaskConical, LayoutGrid, ScanSearch, Settings2, TriangleAlert, WifiOff } from 'lucide-react'
import { useApp } from '@/lib/store'
import { needsHelp } from '@/lib/format'
import { cn } from '@/lib/cn'
import { StatusDot } from '@/components/ui/base'
import { Menu } from '@/components/ui/overlay'
import { Brand } from './Brand'

const link = ({ isActive }: { isActive: boolean }) => cn(
  'relative flex h-11 items-center gap-3 rounded-ctl px-3 text-[15px] font-medium transition-colors max-[1100px]:justify-center',
  isActive ? 'bg-primary-soft text-fg' : 'text-muted hover:bg-surface/60 hover:text-fg',
)

export function Sidebar() {
  const nav = useNavigate()
  const total = useApp((s) => s.instances.length)
  const help = useApp((s) => s.instances.filter((i) => needsHelp(i, s.settings.autoSolve)).length)
  const license = useApp((s) => s.license)
  const server = useApp((s) => s.server)
  const simulateCaptcha = useApp((s) => s.simulateCaptcha)
  const setUpdate = useApp((s) => s.setUpdate)

  return (
    <aside className="flex w-[216px] shrink-0 flex-col bg-surface-2/50 px-3 pb-3 pt-4 max-[1100px]:w-[68px]">
      <div className="px-2 pb-5 max-[1100px]:hidden"><Brand /></div>
      <div className="hidden px-2 pb-5 max-[1100px]:block"><Brand compact /></div>

      <nav aria-label="เมนูหลัก" className="flex flex-col gap-1">
        <NavLink to="/accounts" title="บัญชี" className={link}>
          <LayoutGrid className="size-[18px] shrink-0" aria-hidden />
          <span className="flex-1 max-[1100px]:hidden">บัญชี</span>
          {help > 0 && (
            <span aria-label={`รอคุณช่วย ${help}`} className="grid h-5 min-w-5 place-items-center rounded-full bg-warn px-1.5 text-[11px] font-semibold text-on-primary max-[1100px]:absolute max-[1100px]:-right-0.5 max-[1100px]:-top-0.5">{help}</span>
          )}
        </NavLink>
        <NavLink to="/settings" title="ตั้งค่า" className={link}>
          <Settings2 className="size-[18px] shrink-0" aria-hidden />
          <span className="max-[1100px]:hidden">ตั้งค่า</span>
        </NavLink>
      </nav>

      <div className="mt-auto flex flex-col gap-2">
        {/* เมนูจำลองเหตุการณ์ของ mock — ไม่มีในแอปจริง */}
        <Menu
          align="left" width={248}
          items={[
            { label: 'Captcha ที่ AI แก้ได้', icon: <ScanSearch />, onSelect: () => simulateCaptcha(undefined, 'jigsaw') },
            { label: 'Captcha ที่ AI แก้ไม่ผ่าน', icon: <TriangleAlert />, onSelect: () => simulateCaptcha(undefined, 'jigsaw', true) },
            { label: 'มีอัปเดตใหม่', icon: <DownloadCloud />, divider: true, onSelect: () => setUpdate({ open: true, mandatory: false }) },
            { label: 'อัปเดตบังคับ', icon: <WifiOff />, onSelect: () => setUpdate({ open: true, mandatory: true }) },
            { label: 'หน้า Design System', icon: <FlaskConical />, divider: true, onSelect: () => nav('/design') },
          ]}
          trigger={({ toggle }) => (
            <button type="button" onClick={toggle} className="flex h-9 items-center gap-2 rounded-ctl px-3 text-[13px] text-subtle transition-colors hover:bg-surface/60 hover:text-fg max-[1100px]:justify-center">
              <FlaskConical className="size-4 shrink-0" aria-hidden /><span className="max-[1100px]:hidden">จำลองเหตุการณ์</span>
            </button>
          )}
        />
        <div className="flex flex-col gap-1.5 rounded-card bg-surface p-3 text-[13px] max-[1100px]:items-center max-[1100px]:p-2">
          <span className="flex items-center gap-2" title={server.connected ? 'เชื่อมต่อแล้ว' : 'ขาดการเชื่อมต่อ'}>
            <StatusDot tone={server.connected ? 'ok' : 'bad'} />
            <span className="max-[1100px]:hidden">{server.connected ? 'เชื่อมต่อแล้ว' : 'กำลังเชื่อมต่อใหม่…'}</span>
          </span>
          <span className="text-muted max-[1100px]:hidden">แพ็กเกจ {license.plan} · ใช้ {total}/{license.maxAccounts} บัญชี</span>
        </div>
      </div>
    </aside>
  )
}
