import { useState } from 'react'
import { Outlet } from 'react-router-dom'
import { useSimulation } from '@/lib/simulation'
import { Sidebar } from './Sidebar'
import { Topbar } from './Topbar'
import { StatusBar } from './StatusBar'
import { Toasts } from './Toasts'
import { CommandPalette } from './CommandPalette'
import { NotificationPanel } from './NotificationPanel'
import { UpdateSheet } from '@/features/update/UpdateSheet'

export function AppShell() {
  useSimulation()
  const [notif, setNotif] = useState(false)
  return (
    <div className="flex h-full flex-col">
      <div className="flex min-h-0 flex-1">
        <Sidebar />
        <div className="flex min-w-0 flex-1 flex-col">
          <Topbar onOpenNotifications={() => setNotif(true)} />
          <main className="min-h-0 flex-1 px-5 pb-4">
            <Outlet />
          </main>
        </div>
      </div>
      <StatusBar />
      <Toasts />
      <CommandPalette />
      <NotificationPanel open={notif} onClose={() => setNotif(false)} />
      <UpdateSheet />
    </div>
  )
}
