import { useEffect, useRef, useState } from 'react'
import { ChevronDown, Plus, RefreshCw, Zap } from 'lucide-react'
import { useShallow } from 'zustand/react/shallow'
import { useApp } from '@/lib/store'
import { Badge, Button, Skeleton, StatusDot } from '@/components/ui/base'
import { Menu } from '@/components/ui/overlay'
import { PageHeader } from '@/components/ui/layout'
import { useFirstLoad } from '../_pages/hooks'
import { ProxyDetail } from './ProxyDetail'
import { ProxyEmpty, ProxyListSkeleton, ProxyRow } from './ProxyList'
import { effectiveProxy, proxyReady, proxySignature } from './proxyUi'

export default function ProxyPage() {
  const loading = useFirstLoad('proxy')
  const proxies = useApp((s) => s.proxies)
  const usage = useApp(useShallow((s) => s.proxies.map((p) => s.instances.filter((i) => i.proxyId === p.id).length)))
  const sig = useApp((s) => proxySignature(s))
  const { addProxy, patchProxy, testProxy, toast } = useApp(useShallow((s) => ({ addProxy: s.addProxy, patchProxy: s.patchProxy, testProxy: s.testProxy, toast: s.toast })))
  const [selId, setSelId] = useState<string | null>(null)
  const [applied, setApplied] = useState<string | null>(null)
  const [applying, setApplying] = useState(false)
  const [syncing, setSyncing] = useState(false)
  const timers = useRef<number[]>([])
  useEffect(() => () => timers.current.forEach(window.clearTimeout), [])
  useEffect(() => { if (applied === null) setApplied(sig) }, [applied, sig])

  const current = proxies.find((p) => p.id === selId) ?? proxies[0]
  const dirty = applied !== null && sig !== applied
  const active = proxies.filter((p) => p.enabled).length
  const passed = proxies.filter((p) => typeof p.test === 'object' && p.test.ok).length
  const failed = proxies.filter((p) => typeof p.test === 'object' && !p.test.ok).length
  const anyTesting = proxies.some((p) => p.test === 'testing')

  const add = () => {
    addProxy()
    const list = useApp.getState().proxies
    setSelId(list[list.length - 1]?.id ?? null)
  }

  const apply = () => {
    const bad = proxies.find((p) => p.enabled && !proxyReady(p))
    if (bad) {
      setSelId(bad.id)
      toast({ tone: 'warn', title: `${bad.name || 'Proxy'} ตั้งค่าไม่ครบ`, detail: 'ใส่ host และ port ก่อน หรือปิดใช้งาน' })
      return
    }
    setApplying(true)
    timers.current.push(window.setTimeout(() => {
      const s = useApp.getState()
      const bridged = s.instances.filter((i) => effectiveProxy(i, s.proxies)).length
      const running = s.instances.filter((i) => i.status === 'running' && effectiveProxy(i, s.proxies)).length
      setApplied(proxySignature(s)); setApplying(false)
      toast({ tone: 'ok', title: 'เปิด Native Proxy แล้ว', detail: `Bridge ${bridged} บัญชี${running ? ` · ${running} บัญชีที่รันอยู่ต้องเริ่มใหม่` : ''}` })
    }, 1200))
  }

  const syncProxifier = () => {
    if (!useApp.getState().settings.proxifierPath.trim()) {
      toast({ tone: 'bad', title: 'ไม่พบ Proxifier.exe', detail: 'ตั้ง path ที่ ตั้งค่า › การจัดเก็บ' })
      return
    }
    setSyncing(true)
    timers.current.push(window.setTimeout(() => {
      setSyncing(false)
      toast({ tone: 'ok', title: 'ซิงก์ Proxifier แล้ว', detail: `เขียน ${active} Proxy ลงไฟล์ .ppx และสั่งโหลด` })
    }, 1500))
  }

  const testAll = () => {
    proxies.filter((p) => p.enabled && p.test !== 'testing').forEach((p, k) =>
      timers.current.push(window.setTimeout(() => testProxy(p.id), k * 160)))
  }

  const toggle = (id: string, enabled: boolean) => patchProxy(id, { enabled })

  return (
    <div className="flex h-full flex-col">
      <PageHeader
        title="Proxy"
        sub="เส้นทางออกเน็ตของแต่ละบัญชี"
        actions={
          <>
            <Button
              variant="primary" className="min-w-52" loading={applying} disabled={proxies.length === 0} icon={<Zap className="size-4" />} onClick={apply}
              title="Native Proxy Bridge เป็นวิธีหลัก: เปิดพอร์ตในเครื่องต่อบัญชี ไม่ต้องติดตั้ง Proxifier"
            >
              {applying ? 'กำลังเปิดใช้' : 'บันทึกและเปิดใช้ Native Proxy'}
            </Button>
            <Button icon={<Plus className="size-4" />} onClick={add}>เพิ่ม Proxy</Button>
            <Menu
              width={240}
              items={[{ label: 'ซิงก์ Proxifier (ทางเลือก)', icon: <RefreshCw />, disabled: syncing, onSelect: syncProxifier }]}
              trigger={({ toggle: t }) => (
                <Button variant="ghost" loading={syncing} onClick={t}>{syncing ? 'กำลังซิงก์' : 'เพิ่มเติม'}{!syncing && <ChevronDown className="size-4" aria-hidden />}</Button>
              )}
            />
          </>
        }
      />

      {loading ? (
        <div className="flex min-h-0 flex-1 gap-3 pb-1" aria-busy="true" aria-label="กำลังโหลด">
          <div className="flex h-full w-[380px] shrink-0 flex-col rounded-card bg-surface pt-3 max-[1180px]:w-[330px]"><Skeleton className="mx-3 mb-3 h-8 w-2/3" /><ProxyListSkeleton /></div>
          <div className="flex h-full min-w-0 flex-1 flex-col gap-4 rounded-card bg-surface p-6">
            <Skeleton className="h-12 w-1/2" /><Skeleton className="h-[68px] w-full" /><Skeleton className="h-40 w-full" /><Skeleton className="h-48 w-full" />
          </div>
        </div>
      ) : proxies.length === 0 ? (
        <div className="min-h-0 flex-1 pb-1"><ProxyEmpty onAdd={add} /></div>
      ) : (
        <div className="flex min-h-0 flex-1 gap-3 pb-1">
          <section aria-label="รายการ Proxy" className="flex h-full w-[380px] shrink-0 flex-col rounded-card bg-surface max-[1180px]:w-[330px]">
            <div className="flex items-center justify-between gap-2 px-4 pb-2 pt-3.5">
              <div className="min-w-0">
                <p className="flex flex-wrap items-center gap-x-3 gap-y-0.5 text-[13px] text-muted">
                  <span className="tnum">ใช้งาน {active}/{proxies.length}</span>
                  <span className="inline-flex items-center gap-1.5 tnum"><StatusDot tone="ok" />ผ่าน {passed}</span>
                  {failed > 0 && <span className="inline-flex items-center gap-1.5 tnum"><StatusDot tone="bad" />ล้มเหลว {failed}</span>}
                </p>
                {dirty && <Badge tone="warn" className="mt-1.5">ยังไม่เปิดใช้</Badge>}
              </div>
              <Button size="sm" variant="ghost" loading={anyTesting} onClick={testAll} className="min-w-[92px]">{anyTesting ? 'ทดสอบ' : 'ทดสอบทั้งหมด'}</Button>
            </div>
            <div className="scroll-y min-h-0 flex-1 px-2 pb-2">
              <ul className="flex flex-col gap-0.5">
                {proxies.map((p, k) => (
                  <li key={p.id}><ProxyRow p={p} active={p.id === current?.id} used={usage[k] ?? 0} onSelect={setSelId} onToggle={toggle} /></li>
                ))}
              </ul>
            </div>
          </section>
          {current && <ProxyDetail key={current.id} proxy={current} usedBy={usage[proxies.indexOf(current)] ?? 0} />}
        </div>
      )}
    </div>
  )
}
