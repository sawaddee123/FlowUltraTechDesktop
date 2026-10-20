import { memo, useMemo } from 'react'
import { History, Puzzle, Shapes } from 'lucide-react'
import type { CaptchaRecord } from '@/lib/types'
import { useApp } from '@/lib/store'
import { ago, captchaLabel, useNow } from '@/lib/format'
import { Badge, Button, EmptyState, Skeleton, type Tone } from '@/components/ui/base'
import { DataTable, Section, tdc } from '@/components/ui/layout'
import { CaptchaPanel } from '@/components/captcha/CaptchaPanel'
import type { TabProps } from './registry'
import { useFakeLoad } from './tools/hooks'

const RESULT: Record<CaptchaRecord['result'], { label: string; tone: Tone }> = {
  solved: { label: 'แก้สำเร็จ', tone: 'ok' },
  failed: { label: 'ล้มเหลว', tone: 'bad' },
  dismissed: { label: 'ข้าม', tone: 'neutral' },
}
const MODE: Record<CaptchaRecord['mode'], string> = { 'ai-auto': 'AI อัตโนมัติ', 'ai-manual': 'AI กดเอง', manual: 'แก้เอง' }

const HEAD = [
  { label: 'เวลา', className: 'w-[120px]' }, { label: 'ชนิด', className: 'w-[110px]' },
  { label: 'ผลลัพธ์', className: 'w-[130px]' }, { label: 'โหมด' }, { label: 'ใช้เวลา', className: 'w-[100px] text-right' },
]

const Row = memo(function Row({ r, now }: { r: CaptchaRecord; now: number }) {
  const res = RESULT[r.result]
  return (
    <tr className="transition-colors hover:bg-surface-2/60">
      <td className={`${tdc} text-sm text-muted`}>{ago(r.at, now)}</td>
      <td className={`${tdc} text-sm`}>{captchaLabel[r.kind]}</td>
      <td className={tdc}><Badge tone={res.tone} dot>{res.label}</Badge></td>
      <td className={`${tdc} text-sm text-muted`}>{MODE[r.mode]}</td>
      <td className={`${tdc} text-right text-sm text-muted tnum`}>{(r.ms / 1000).toFixed(1)} วิ</td>
    </tr>
  )
})

export default function CaptchaTab({ instance }: TabProps) {
  const history = useApp((s) => s.history)
  const simulate = useApp((s) => s.simulateCaptcha)
  const ready = useFakeLoad(380)
  const now = useNow(5000)
  const mine = useMemo(
    () => history.filter((h) => h.instanceId === instance.id).sort((a, b) => b.at - a.at).slice(0, 200),
    [history, instance.id],
  )
  const busy = !!instance.captcha

  return (
    <div className="flex h-full min-h-0 flex-col gap-5 overflow-y-auto px-6 py-4">
      <CaptchaPanel instance={instance} scope="instance" />

      <Section
        title="ประวัติของบัญชีนี้"
        aside={
          <div className="flex items-center gap-2">
            <Button size="sm" disabled={busy} title={busy ? 'มี Captcha ค้างอยู่แล้ว' : undefined}
              icon={<Puzzle className="size-3.5" />} onClick={() => simulate(instance.id, 'jigsaw')}>จำลอง Jigsaw</Button>
            <Button size="sm" disabled={busy} title={busy ? 'มี Captcha ค้างอยู่แล้ว' : undefined}
              icon={<Shapes className="size-3.5" />} onClick={() => simulate(instance.id, 'symbol')}>จำลอง Symbol</Button>
          </div>
        }
      >
        {!ready ? (
          <div className="flex flex-col gap-3" aria-busy="true" aria-label="กำลังโหลดประวัติ">
            {Array.from({ length: 4 }, (_, i) => <Skeleton key={i} className="h-9 w-full" />)}
          </div>
        ) : mine.length === 0 ? (
          <EmptyState icon={<History className="size-5" aria-hidden />} title="ยังไม่มีประวัติ Captcha"
            hint="กดจำลอง Jigsaw หรือ Symbol เพื่อดูการทำงาน" />
        ) : (
          <DataTable head={HEAD}>{mine.map((r) => <Row key={r.id} r={r} now={now} />)}</DataTable>
        )}
      </Section>
    </div>
  )
}
