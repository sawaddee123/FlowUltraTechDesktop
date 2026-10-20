import { useMemo, useState } from 'react'
import { ShieldCheck, Puzzle, Shapes } from 'lucide-react'
import { useShallow } from 'zustand/react/shallow'
import { useApp } from '@/lib/store'
import type { CaptchaKind, CaptchaRecord } from '@/lib/types'
import { ago, captchaLabel, displayName } from '@/lib/format'
import { Badge, Button, EmptyState, Segmented, Skeleton, Stat, StatusDot } from '@/components/ui/base'
import { Select } from '@/components/ui/form'
import { DataTable, PageHeader, Surface, tdc } from '@/components/ui/layout'
import { CaptchaPanel } from '@/components/captcha/CaptchaPanel'
import { cn } from '@/lib/cn'
import { useFirstLoad } from '../_pages/hooks'

const resultMeta = {
  solved: { label: 'สำเร็จ', tone: 'ok' },
  failed: { label: 'ล้มเหลว', tone: 'bad' },
  dismissed: { label: 'ข้าม', tone: 'neutral' },
} as const
const modeLabel: Record<CaptchaRecord['mode'], string> = { 'ai-auto': 'AI อัตโนมัติ', 'ai-manual': 'AI กดเอง', manual: 'ทำเอง' }

export default function CaptchaCenter() {
  const loading = useFirstLoad('captcha')
  const { instances, history, simulateCaptcha } = useApp(useShallow((s) => ({ instances: s.instances, history: s.history, simulateCaptcha: s.simulateCaptcha })))
  const [sel, setSel] = useState<string | null>(null)
  const [kind, setKind] = useState<'all' | CaptchaKind>('all')
  const [result, setResult] = useState<'all' | CaptchaRecord['result']>('all')
  const [mode, setMode] = useState<'all' | CaptchaRecord['mode']>('all')

  const alerts = instances.filter((i) => i.captcha)
  const current = alerts.find((i) => i.id === sel) ?? alerts[0]
  const rows = useMemo(
    () => history.filter((h) => (kind === 'all' || h.kind === kind) && (result === 'all' || h.result === result) && (mode === 'all' || h.mode === mode)).slice(0, 100),
    [history, kind, result, mode],
  )
  const total = history.length
  const solved = history.filter((h) => h.result === 'solved')
  const rate = total ? Math.round((solved.length / total) * 100) : 0
  const avg = solved.length ? solved.reduce((a, h) => a + h.ms, 0) / solved.length / 1000 : 0
  const name = (id: string) => { const i = instances.find((x) => x.id === id); return i ? displayName(i) : id }

  return (
    <div className="flex h-full flex-col">
      <PageHeader
        title="Captcha"
        sub="แจ้งเตือนและประวัติจากทุกบัญชี"
        actions={
          <>
            <Button icon={<Puzzle className="size-4" />} onClick={() => simulateCaptcha(undefined, 'jigsaw')}>จำลอง Jigsaw</Button>
            <Button icon={<Shapes className="size-4" />} onClick={() => simulateCaptcha(undefined, 'symbol')}>จำลอง Symbol</Button>
          </>
        }
      />
      {loading ? (
        <div className="flex min-h-0 flex-1 gap-3 pb-1" aria-busy="true" aria-label="กำลังโหลด">
          <div className="flex w-[380px] shrink-0 flex-col gap-3 rounded-card bg-surface p-4"><Skeleton className="h-16 w-full" /><Skeleton className="h-16 w-full" /></div>
          <div className="flex min-w-0 flex-1 flex-col gap-3 rounded-card bg-surface p-5"><Skeleton className="h-10 w-1/2" /><Skeleton className="h-64 w-full" /></div>
        </div>
      ) : (
        <div className="flex min-h-0 flex-1 gap-3 pb-1">
          <section aria-label="Captcha ที่รอแก้" className="flex w-[380px] shrink-0 flex-col rounded-card bg-surface max-[1180px]:w-[330px]">
            <div className="flex items-center justify-between px-4 pb-2 pt-3.5">
              <p className="text-[13px] font-medium text-muted">รอแก้ตอนนี้</p>
              <Badge tone={alerts.length ? 'bad' : 'ok'} dot>{alerts.length ? `${alerts.length} รายการ` : 'ไม่มี'}</Badge>
            </div>
            <div className="scroll-y min-h-0 flex-1 px-2 pb-2">
              {alerts.length === 0 ? (
                <EmptyState
                  icon={<ShieldCheck />} title="ไม่มี Captcha ค้าง"
                  hint="เมื่อบัญชีที่รันอยู่เจอ Captcha จะขึ้นที่นี่พร้อมนับถอยหลัง ลองจำลองเพื่อดูหน้าตา"
                  action={<Button variant="primary" onClick={() => simulateCaptcha()}>จำลอง Captcha</Button>}
                />
              ) : (
                <ul className="flex flex-col gap-0.5">
                  {alerts.map((i) => (
                    <li key={i.id}>
                      <button
                        type="button" onClick={() => setSel(i.id)} aria-current={i.id === current?.id}
                        className={cn('flex w-full items-center gap-3 rounded-ctl px-3 py-2.5 text-left transition-colors', i.id === current?.id ? 'bg-primary-soft' : 'hover:bg-surface-2')}
                      >
                        <StatusDot tone="bad" pulse />
                        <span className="min-w-0 flex-1">
                          <span className="block truncate text-sm font-medium">{displayName(i)}</span>
                          <span className="block text-xs text-subtle">{captchaLabel[i.captcha!.kind]} · {ago(i.captcha!.detectedAt)}</span>
                        </span>
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </section>

          <div className="scroll-y flex min-w-0 flex-1 flex-col gap-3">
            {current && <CaptchaPanel key={current.captcha!.id} instance={current} scope="system" />}
            <Surface className="p-5">
              <div className="grid grid-cols-4 gap-4 max-[1180px]:grid-cols-2">
                <Stat label="ทั้งหมด" value={total} />
                <Stat label="อัตราสำเร็จ" value={`${rate}%`} tone={rate >= 80 ? 'ok' : rate >= 50 ? 'warn' : 'bad'} />
                <Stat label="เวลาเฉลี่ยที่สำเร็จ" value={`${avg.toFixed(1)} วินาที`} />
                <Stat label="ล้มเหลว" value={history.filter((h) => h.result === 'failed').length} tone="bad" />
              </div>
            </Surface>
            <Surface className="pb-2">
              <div className="flex flex-wrap items-center gap-2 px-4 py-3">
                <h2 className="mr-auto text-[13px] font-semibold text-muted">ประวัติ</h2>
                <Segmented size="sm" label="ชนิด" value={kind} onChange={setKind} options={[{ value: 'all', label: 'ทุกชนิด' }, { value: 'jigsaw', label: 'Jigsaw' }, { value: 'symbol', label: 'Symbol' }, { value: 'inactivity', label: 'Inactivity' }]} />
                <div className="w-32"><Select aria-label="ผลลัพธ์" value={result} onChange={(e) => setResult(e.target.value as typeof result)}><option value="all">ทุกผล</option><option value="solved">สำเร็จ</option><option value="failed">ล้มเหลว</option><option value="dismissed">ข้าม</option></Select></div>
                <div className="w-36"><Select aria-label="โหมด" value={mode} onChange={(e) => setMode(e.target.value as typeof mode)}><option value="all">ทุกโหมด</option><option value="ai-auto">AI อัตโนมัติ</option><option value="ai-manual">AI กดเอง</option><option value="manual">ทำเอง</option></Select></div>
              </div>
              {rows.length === 0 ? (
                <EmptyState
                  icon={<ShieldCheck />} title={total ? 'ไม่พบรายการตามตัวกรอง' : 'ยังไม่มีประวัติ'}
                  hint={total ? 'ลองล้างตัวกรองเพื่อดูทั้งหมด' : 'ประวัติจะบันทึกเองหลังมี Captcha ครั้งแรก'}
                  action={total ? <Button onClick={() => { setKind('all'); setResult('all'); setMode('all') }}>ล้างตัวกรอง</Button> : undefined}
                />
              ) : (
                <DataTable head={[{ label: 'บัญชี' }, { label: 'ชนิด' }, { label: 'ผล' }, { label: 'โหมด' }, { label: 'ใช้เวลา', className: 'text-right' }, { label: 'เมื่อ' }]}>
                  {rows.map((h) => (
                    <tr key={h.id} className="hover:bg-surface-2">
                      <td className={cn(tdc, 'font-medium')}>{name(h.instanceId)}</td>
                      <td className={tdc}>{captchaLabel[h.kind]}</td>
                      <td className={tdc}><Badge tone={resultMeta[h.result].tone} dot>{resultMeta[h.result].label}</Badge></td>
                      <td className={cn(tdc, 'text-muted')}>{modeLabel[h.mode]}</td>
                      <td className={cn(tdc, 'text-right tnum text-muted')}>{h.ms ? `${(h.ms / 1000).toFixed(1)} วิ.` : '-'}</td>
                      <td className={cn(tdc, 'text-subtle')}>{ago(h.at)}</td>
                    </tr>
                  ))}
                </DataTable>
              )}
            </Surface>
          </div>
        </div>
      )}
    </div>
  )
}
