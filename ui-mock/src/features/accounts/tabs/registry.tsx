import { lazy, type ComponentType, type LazyExoticComponent } from 'react'
import type { Instance } from '@/lib/types'

export type TabId = 'live' | 'overlay' | 'reply'
export type AdvancedId = 'session' | 'logs' | 'network' | 'script' | 'windows'
export type TabProps = { instance: Instance }
type Lazy = LazyExoticComponent<ComponentType<TabProps>>

/** แต่ละแท็บเป็น lazy chunk แยก — เปิดแท็บไหนค่อยโหลดแท็บนั้น */
export const tabComponents: Record<TabId, Lazy> = {
  live: lazy(() => import('./LiveTab')),
  overlay: lazy(() => import('./OverlayTab')),
  reply: lazy(() => import('./ReplyTab')),
}

export const advancedComponents: Record<AdvancedId, Lazy> = {
  session: lazy(() => import('./SessionTab')),
  logs: lazy(() => import('./LogsTab')),
  network: lazy(() => import('./NetworkTab')),
  script: lazy(() => import('./ScriptTab')),
  windows: lazy(() => import('./WindowsTab')),
}

/** บอทตอบแชทปิดไว้ก่อน (เร็วๆ นี้) — เปิดกลับได้โดยเอา disabled ออก */
export const mainTabs: { id: TabId; label: string; disabled?: boolean }[] = [
  { id: 'live', label: 'ภาพรวม' },
  { id: 'overlay', label: 'หน้าจอไลฟ์' },
  { id: 'reply', label: 'บอทตอบแชท', disabled: true },
]

export const advancedTabs: { id: AdvancedId; label: string }[] = [
  { id: 'session', label: 'เซสชัน' },
  { id: 'logs', label: 'บันทึก' },
  { id: 'network', label: 'Network API' },
  { id: 'script', label: 'สคริปต์' },
  { id: 'windows', label: 'หน้าต่าง' },
]

export const isTabId = (v?: string): v is TabId => !!v && v in tabComponents && !mainTabs.find((t) => t.id === v)?.disabled
export const isAdvancedId = (v?: string): v is AdvancedId => !!v && v in advancedComponents
