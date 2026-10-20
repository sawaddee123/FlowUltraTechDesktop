import { CircleCheck, CircleX, CircleDashed } from 'lucide-react'
import type { Instance, ProxyItem } from '@/lib/types'
import { Spinner } from '@/components/ui/base'
import { cn } from '@/lib/cn'

export const protocols = ['Socks5', 'Http', 'Https'] as const

/** host/port ใช้งานได้ตามเงื่อนไขของ Bridge (host ไม่ว่าง, port 1–65535) */
export const proxyReady = (p: ProxyItem) => !!p.host.trim() && Number.isFinite(p.port) && p.port >= 1 && p.port <= 65535

/** ลำดับตัดสินของ Bridge: Proxy เฉพาะบอท → รายการกลางที่เปิดอยู่ → ตรง */
export function effectiveProxy(i: Instance, proxies: ProxyItem[]): 'custom' | ProxyItem | null {
  if (i.botProxy.enabled && i.botProxy.host.trim() && i.botProxy.port > 0) return 'custom'
  const p = proxies.find((x) => x.id === i.proxyId)
  return p && p.enabled && proxyReady(p) ? p : null
}

export function proxySignature(s: { proxies: ProxyItem[]; instances: Instance[] }) {
  return (
    s.proxies.map((p) => [p.id, p.protocol, p.host, p.port, p.username, p.password, p.enabled].join(':')).join('|') +
    '#' + s.instances.map((i) => `${i.id}:${i.proxyId ?? ''}`).join('|')
  )
}

/** สถานะผลทดสอบ: ไอคอน + ข้อความ (ไม่ใช้สีอย่างเดียว) */
export function TestPill({ test, className }: { test: ProxyItem['test']; className?: string }) {
  const base = 'inline-flex items-center gap-1 whitespace-nowrap text-xs'
  if (test === 'testing') return <span className={cn(base, 'text-muted', className)}><Spinner className="size-3" />กำลังทดสอบ</span>
  if (!test) return <span className={cn(base, 'text-subtle', className)}><CircleDashed className="size-3" aria-hidden />ยังไม่ทดสอบ</span>
  if (test.ok) return <span className={cn(base, 'text-ok', className)}><CircleCheck className="size-3" aria-hidden />ผ่าน · {test.pingMs} ms</span>
  return <span className={cn(base, 'text-bad', className)}><CircleX className="size-3" aria-hidden />ล้มเหลว</span>
}
