import { lazy, Suspense } from 'react'
import { HashRouter, Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { useApp } from '@/lib/store'
import { AppShell } from '@/components/shell/AppShell'
import { Skeleton } from '@/components/ui/base'
import Login from '@/features/auth/Login'

// หน้าหลักทุกหน้าเป็น lazy chunk — โหลดเฉพาะหน้าที่เปิด
const AccountsPage = lazy(() => import('@/features/accounts/AccountsPage'))
const SettingsPage = lazy(() => import('@/features/settings/SettingsPage'))
const DesignPage = lazy(() => import('@/features/design/DesignPage'))

function PageFallback() {
  return (
    <div className="flex flex-col gap-4 px-10 pt-8" aria-busy="true" aria-label="กำลังโหลด">
      <Skeleton className="h-9 w-56" />
      <Skeleton className="h-28 w-full rounded-card" />
      <Skeleton className="h-64 w-full rounded-card" />
    </div>
  )
}

function Guard({ children }: { children: React.ReactNode }) {
  const session = useApp((s) => s.session)
  return session === 'in' ? <>{children}</> : <Navigate to="/login" replace />
}

export default function App() {
  return (
    <HashRouter>
      <Routes>
        <Route path="/login" element={<Login />} />
        <Route element={<Guard><AppShell /></Guard>}>
          <Route index element={<Navigate to="/accounts" replace />} />
          <Route path="/accounts/:id?/:tab?/:sub?" element={<Suspense fallback={<PageFallback />}><AccountsPage /></Suspense>} />
          <Route path="/settings/:section?" element={<Suspense fallback={<PageFallback />}><SettingsPage /></Suspense>} />
          <Route path="/design" element={<Suspense fallback={<PageFallback />}><DesignPage /></Suspense>} />
        </Route>
        <Route path="*" element={<Navigate to="/accounts" replace />} />
      </Routes>
    </HashRouter>
  )
}

