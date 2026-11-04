import { Outlet } from 'react-router-dom'
import { useSimulation } from '@/lib/simulation'
import { Sidebar } from './Sidebar'
import { Toasts } from './Toasts'
import { CommandPalette } from './CommandPalette'
import { UpdateSheet } from '@/features/update/UpdateSheet'
import { AddAccountDialog } from '@/features/accounts/AddAccountDialog'
import { CaptchaHelpDialog } from '@/features/accounts/CaptchaHelpDialog'

export function AppShell() {
  useSimulation()
  return (
    <div className="flex h-full">
      <Sidebar />
      <main className="min-h-0 min-w-0 flex-1 overflow-y-auto">
        <Outlet />
      </main>
      <Toasts />
      <CommandPalette />
      <UpdateSheet />
      <AddAccountDialog />
      <CaptchaHelpDialog />
    </div>
  )
}
