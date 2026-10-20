import { useNavigate } from 'react-router-dom'
import { Bell, FlaskConical, Moon, ScanSearch, Search, Sun, DownloadCloud, TriangleAlert } from 'lucide-react'
import { useApp, selectRunning } from '@/lib/store'
import { IconButton, Kbd, Meter } from '@/components/ui/base'
import { Menu } from '@/components/ui/overlay'

export function Topbar({ onOpenNotifications }: { onOpenNotifications: () => void }) {
  const nav = useNavigate()
  const theme = useApp((s) => s.theme)
  const setTheme = useApp((s) => s.setTheme)
  const unread = useApp((s) => s.notifications.filter((n) => !n.read).length)
  const running = useApp(selectRunning)
  const max = useApp((s) => s.license.maxAccounts)
  const setPalette = useApp((s) => s.setPalette)
  const simulateCaptcha = useApp((s) => s.simulateCaptcha)
  const setUpdate = useApp((s) => s.setUpdate)
  const push = useApp((s) => s.push)

  return (
    <header className="flex h-14 shrink-0 items-center gap-3 px-5">
      <button
        type="button" onClick={() => setPalette(true)}
        className="flex h-9 w-full max-w-md items-center gap-2.5 rounded-ctl bg-surface px-3 text-left text-sm text-subtle transition-colors hover:bg-surface-2"
      >
        <Search className="size-4" aria-hidden />
        <span className="flex-1">ค้นหาบัญชีหรือสั่งงาน</span>
        <Kbd>Ctrl K</Kbd>
      </button>

      <div className="ml-auto flex items-center gap-1">
        <div className="mr-2 hidden w-36 sm:block" title={`เปิดอยู่ ${running} จาก ${max} จอ`}>
          <div className="mb-1 flex justify-between text-xs">
            <span className="text-subtle">โควต้าจอ</span>
            <span className="font-medium tnum">{running}/{max}</span>
          </div>
          <Meter value={running} max={max} tone={running >= max ? 'warn' : 'info'} />
        </div>

        <Menu
          width={240}
          items={[
            { label: 'จำลอง Jigsaw Captcha', icon: <ScanSearch />, onSelect: () => simulateCaptcha(undefined, 'jigsaw') },
            { label: 'จำลอง Symbol Captcha', icon: <ScanSearch />, onSelect: () => simulateCaptcha(undefined, 'symbol') },
            { label: 'จำลองอัปเดตใหม่', icon: <DownloadCloud />, divider: true, onSelect: () => setUpdate({ open: true, mandatory: false }) },
            { label: 'จำลองอัปเดตบังคับ', icon: <TriangleAlert />, onSelect: () => setUpdate({ open: true, mandatory: true }) },
            { label: 'ดูหน้า Design System', icon: <FlaskConical />, divider: true, onSelect: () => nav('/design') },
            { label: 'เพิ่มแจ้งเตือนตัวอย่าง', icon: <Bell />, onSelect: () => push({ tone: 'info', title: 'ทดสอบแจ้งเตือน', detail: 'ข้อความตัวอย่าง' }, true) },
          ]}
          trigger={({ toggle }) => <IconButton label="จำลองเหตุการณ์ (mock)" onClick={toggle}><FlaskConical className="size-[18px]" /></IconButton>}
        />

        <span className="relative">
          <IconButton label="การแจ้งเตือน" onClick={onOpenNotifications}><Bell className="size-[18px]" /></IconButton>
          {unread > 0 && (
            <span aria-label={`ยังไม่อ่าน ${unread}`} className="pointer-events-none absolute right-1 top-1 grid h-4 min-w-4 place-items-center rounded-full bg-bad px-1 text-[10px] font-semibold text-on-primary">{unread}</span>
          )}
        </span>

        <IconButton label={theme === 'dark' ? 'ธีมสว่าง' : 'ธีมมืด'} onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}>
          {theme === 'dark' ? <Sun className="size-[18px]" /> : <Moon className="size-[18px]" />}
        </IconButton>
      </div>
    </header>
  )
}
