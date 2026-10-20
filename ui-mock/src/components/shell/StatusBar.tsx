import { Activity, Cpu } from 'lucide-react'
import { useApp, selectRunning } from '@/lib/store'
import { StatusDot } from '@/components/ui/base'

export function StatusBar() {
  const status = useApp((s) => s.status)
  const server = useApp((s) => s.server)
  const running = useApp(selectRunning)
  const total = useApp((s) => s.instances.length)
  const proxies = useApp((s) => s.proxies.length)
  const rules = useApp((s) => s.rules.length)
  return (
    <footer className="flex h-7 shrink-0 items-center gap-4 bg-surface-2/50 px-5 text-xs text-subtle">
      <span className="truncate" aria-live="polite">{status}</span>
      <span className="ml-auto flex shrink-0 items-center gap-4">
        <span className="flex items-center gap-1.5">
          <StatusDot tone={server.connected ? 'ok' : 'bad'} />
          {server.connected ? `Realtime · ${server.latencyMs} ms` : 'ออฟไลน์ • กำลังลองเชื่อมต่อใหม่'}
        </span>
        <span className="hidden sm:inline">บัญชี {running}/{total} · Proxy {proxies} · กฎ {rules}</span>
        <span className="hidden items-center gap-1.5 md:flex"><Cpu className="size-3.5" aria-hidden />CDP Core 1.7.0</span>
        <span className="hidden items-center gap-1.5 md:flex"><Activity className="size-3.5" aria-hidden />v1.7.0</span>
      </span>
    </footer>
  )
}
