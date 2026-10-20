import { useEffect, useState } from 'react'
import type { Instance, InstanceStatus, CaptchaKind } from './types'
import type { Tone } from '@/components/ui/base'

export const fmtNum = (n: number) => (n >= 10_000 ? `${(n / 1000).toFixed(1)}K` : n.toLocaleString('en-US'))

export const fmtTime = (t: number, seconds = true) =>
  new Date(t).toLocaleTimeString('th-TH', { hour12: false, hour: '2-digit', minute: '2-digit', ...(seconds ? { second: '2-digit' } : {}) })

export function ago(t: number, now = Date.now()) {
  const s = Math.max(0, Math.round((now - t) / 1000))
  if (s < 5) return 'เมื่อสักครู่'
  if (s < 60) return `${s} วิ.ก่อน`
  const m = Math.round(s / 60)
  if (m < 60) return `${m} นาทีก่อน`
  const h = Math.round(m / 60)
  return h < 24 ? `${h} ชม.ก่อน` : `${Math.round(h / 24)} วันก่อน`
}

export function uptime(since?: number, now = Date.now()) {
  if (!since) return '—'
  const m = Math.floor((now - since) / 60000)
  return m < 60 ? `${m} น.` : `${Math.floor(m / 60)} ชม. ${m % 60} น.`
}

export const statusMeta: Record<InstanceStatus, { label: string; tone: Tone }> = {
  running: { label: 'ทำงาน', tone: 'ok' },
  starting: { label: 'กำลังเริ่ม', tone: 'warn' },
  error: { label: 'ผิดพลาด', tone: 'bad' },
  stopped: { label: 'หยุด', tone: 'neutral' },
}

export const captchaLabel: Record<CaptchaKind, string> = { jigsaw: 'Jigsaw', symbol: 'Symbol', inactivity: 'Inactivity' }

export const displayName = (i: Instance) => i.nickname || i.name

/** นาฬิกากลาง 1 วินาที — ใช้ให้ "x วินาทีก่อน" ขยับโดยไม่เรนเดอร์ทั้งแอป */
export function useNow(intervalMs = 1000) {
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), intervalMs)
    return () => window.clearInterval(id)
  }, [intervalMs])
  return now
}
