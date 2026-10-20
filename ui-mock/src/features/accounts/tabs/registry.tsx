import { lazy, type ComponentType, type LazyExoticComponent } from 'react'
import type { Instance } from '@/lib/types'

export type TabId = 'live' | 'reply' | 'captcha' | 'overlay' | 'session' | 'logs' | 'network' | 'script' | 'windows'
export type TabProps = { instance: Instance }

/** แต่ละแท็บเป็น lazy chunk แยก — เปิดแท็บไหนค่อยโหลดแท็บนั้น */
export const tabComponents: Record<TabId, LazyExoticComponent<ComponentType<TabProps>>> = {
  live: lazy(() => import('./LiveTab')),
  reply: lazy(() => import('./ReplyTab')),
  captcha: lazy(() => import('./CaptchaTab')),
  overlay: lazy(() => import('./OverlayTab')),
  session: lazy(() => import('./SessionTab')),
  logs: lazy(() => import('./LogsTab')),
  network: lazy(() => import('./NetworkTab')),
  script: lazy(() => import('./ScriptTab')),
  windows: lazy(() => import('./WindowsTab')),
}

export const mainTabs: { id: TabId; label: string }[] = [
  { id: 'live', label: 'แชทสด' },
  { id: 'reply', label: 'ตอบอัตโนมัติ' },
  { id: 'captcha', label: 'Captcha' },
  { id: 'overlay', label: 'Overlay' },
  { id: 'session', label: 'เซสชัน' },
  { id: 'logs', label: 'บันทึก' },
]

export const toolTabs: { id: TabId; label: string }[] = [
  { id: 'network', label: 'Network API' },
  { id: 'script', label: 'สคริปต์' },
  { id: 'windows', label: 'หน้าต่าง (Handles)' },
]

export const isTabId = (v?: string): v is TabId => !!v && v in tabComponents
