import { useState, type ReactNode } from 'react'
import { Bell, Plus, Search, Trash2 } from 'lucide-react'
import { useApp } from '@/lib/store'
import {
  Avatar, Badge, Button, CopyButton, EmptyState, IconButton, Kbd, Meter, Segmented, Skeleton, Spinner, Stat, StatusDot, Switch,
} from '@/components/ui/base'
import { Field, Input, NumberInput, Select, SecretInput, Slider, Textarea, ToggleRow, ColorInput } from '@/components/ui/form'
import { Confirm, Menu, Sheet } from '@/components/ui/overlay'
import { DataTable, PageHeader, Section, Surface, Tabs, tdc } from '@/components/ui/layout'
import { cn } from '@/lib/cn'

const swatches = [
  ['bg', 'bg-bg'], ['surface', 'bg-surface'], ['surface-2', 'bg-surface-2'], ['surface-3', 'bg-surface-3'],
  ['primary', 'bg-primary'], ['primary-soft', 'bg-primary-soft'], ['ok', 'bg-ok'], ['ok-soft', 'bg-ok-soft'],
  ['warn', 'bg-warn'], ['warn-soft', 'bg-warn-soft'], ['bad', 'bg-bad'], ['bad-soft', 'bg-bad-soft'],
  ['line', 'bg-line'], ['fg', 'bg-fg'], ['muted', 'bg-muted'], ['subtle', 'bg-subtle'],
] as const

function Demo({ title, children }: { title: string; children: ReactNode }) {
  return (
    <Section title={title}>
      <Surface className="flex flex-wrap items-center gap-3 p-5">{children}</Surface>
    </Section>
  )
}

export default function DesignPage() {
  const toast = useApp((s) => s.toast)
  const theme = useApp((s) => s.theme)
  const setTheme = useApp((s) => s.setTheme)
  const [sw, setSw] = useState(true)
  const [seg, setSeg] = useState<'a' | 'b' | 'c'>('a')
  const [tab, setTab] = useState<'one' | 'two' | 'three'>('one')
  const [num, setNum] = useState(10)
  const [vol, setVol] = useState(60)
  const [color, setColor] = useState('#3b82f6')
  const [sheet, setSheet] = useState(false)
  const [confirm, setConfirm] = useState(false)
  const [busy, setBusy] = useState(false)

  return (
    <div className="scroll-y h-full">
      <PageHeader
        title="Design System" sub="โทเคนและคอมโพเนนต์ที่ใช้ร่วมทั้งแอป"
        actions={<Segmented label="ธีม" value={theme} onChange={setTheme} options={[{ value: 'dark', label: 'มืด' }, { value: 'light', label: 'สว่าง' }]} />}
      />
      <div className="px-6 pb-10">
        <Section title="สี">
          <div className="grid grid-cols-8 gap-3 max-[1180px]:grid-cols-4">
            {swatches.map(([n, c]) => (
              <div key={n}>
                <div className={cn('h-14 rounded-ctl hairline', c)} />
                <p className="mt-1 text-xs text-muted">{n}</p>
              </div>
            ))}
          </div>
        </Section>

        <Section title="ตัวอักษร">
          <Surface className="flex flex-col gap-2 p-5">
            <p className="text-xl font-semibold tracking-tight">หัวข้อหน้า text-xl / 600</p>
            <p className="text-base font-semibold">หัวข้อส่วน text-base / 600</p>
            <p className="text-sm">เนื้อหาปกติ text-sm สำหรับข้อความทั่วไป</p>
            <p className="text-[13px] text-muted">ข้อความรอง 13px สีรอง</p>
            <p className="text-xs text-subtle">เมตาดาต้า text-xs สี subtle</p>
            <p className="font-mono text-[13px]">JetBrains Mono · 0x00A4F2 · https://example.test</p>
            <p className="tnum text-sm">ตัวเลขเท่ากว้าง 1,234,567.89</p>
          </Surface>
        </Section>

        <Demo title="ปุ่ม">
          <Button variant="primary" icon={<Plus className="size-4" />}>หลัก</Button>
          <Button>รอง</Button>
          <Button variant="ghost">เบา</Button>
          <Button variant="danger" icon={<Trash2 className="size-4" />}>ลบ</Button>
          <Button size="sm">เล็ก</Button>
          <Button disabled>ปิดใช้</Button>
          <Button variant="primary" className="min-w-28" loading={busy} onClick={() => { setBusy(true); window.setTimeout(() => setBusy(false), 1500) }}>{busy ? 'กำลังทำ' : 'กดให้โหลด'}</Button>
          <IconButton label="แจ้งเตือน"><Bell className="size-4" /></IconButton>
          <CopyButton text="Studio Live" />
        </Demo>

        <Demo title="ป้ายสถานะ">
          <Badge>กลาง</Badge><Badge tone="info" dot>ข้อมูล</Badge><Badge tone="ok" dot>สำเร็จ</Badge><Badge tone="warn" dot>เตือน</Badge><Badge tone="bad" dot>ผิดพลาด</Badge>
          <span className="inline-flex items-center gap-2 text-sm"><StatusDot tone="ok" pulse />ออนไลน์</span>
          <span className="inline-flex items-center gap-2 text-sm"><StatusDot tone="bad" />ขัดข้อง</span>
          <Kbd>Ctrl K</Kbd>
        </Demo>

        <Demo title="ช่องกรอก">
          <div className="grid w-full grid-cols-3 gap-4 max-[1180px]:grid-cols-1">
            <Field label="ข้อความ" hint="คำใบ้อยู่ใน tooltip"><Input placeholder="พิมพ์ที่นี่" /></Field>
            <Field label="ผิดพลาด" error="ต้องไม่ว่าง"><Input aria-invalid defaultValue="" /></Field>
            <Field label="ปิดใช้"><Input disabled value="ปิดอยู่" readOnly /></Field>
            <Field label="ตัวเลข"><NumberInput value={num} onChange={setNum} min={0} max={100} suffix="วิ." /></Field>
            <Field label="รหัสลับ"><SecretInput defaultValue="secret-value" /></Field>
            <Field label="ตัวเลือก"><Select defaultValue="b"><option value="a">ตัวเลือก A</option><option value="b">ตัวเลือก B</option></Select></Field>
            <Field label="สไลเดอร์"><Slider label="ระดับ" value={vol} onChange={setVol} /></Field>
            <Field label="สี"><ColorInput label="สี" value={color} onChange={setColor} /></Field>
            <Field label="หลายบรรทัด"><Textarea placeholder="ข้อความยาว" /></Field>
          </div>
        </Demo>

        <Demo title="สวิตช์ ตัวเลือก และแท็บ">
          <Switch checked={sw} onChange={setSw} label="สวิตช์" />
          <Switch checked={sw} onChange={setSw} label="สวิตช์เล็ก" size="sm" />
          <Switch checked disabled onChange={() => undefined} label="ปิดใช้" />
          <Segmented label="ตัวอย่าง" value={seg} onChange={setSeg} options={[{ value: 'a', label: 'หนึ่ง' }, { value: 'b', label: 'สอง' }, { value: 'c', label: 'สาม' }]} />
          <div className="w-full">
            <Tabs label="แท็บตัวอย่าง" value={tab} onChange={setTab} tabs={[{ id: 'one', label: 'ภาพรวม', icon: <Search /> }, { id: 'two', label: 'บันทึก', badge: <Badge tone="bad">3</Badge> }, { id: 'three', label: 'ตั้งค่า' }]} />
            <p className="px-3 py-3 text-sm text-muted">เนื้อหาแท็บ {tab}</p>
          </div>
          <div className="w-full"><ToggleRow title="แถวสวิตช์" sub="คำอธิบายสั้นหนึ่งบรรทัด"><Switch checked={sw} onChange={setSw} label="แถวสวิตช์" /></ToggleRow></div>
        </Demo>

        <Demo title="ข้อมูล">
          <div className="grid w-full grid-cols-4 gap-4"><Stat label="ผู้ชม" value="1,284" sub="+12%" /><Stat label="สำเร็จ" value="96%" tone="ok" /><Stat label="เตือน" value="3" tone="warn" /><Stat label="ล้มเหลว" value="1" tone="bad" /></div>
          <div className="grid w-full grid-cols-3 gap-4"><Meter value={30} max={100} /><Meter value={65} max={100} tone="ok" /><Meter value={92} max={100} tone="bad" /></div>
          <Avatar name="มะลิ" hue={20} /><Avatar name="Baan" hue={200} size={32} />
          <div className="w-full">
            <DataTable head={[{ label: 'ชื่อ' }, { label: 'สถานะ' }, { label: 'ค่า', className: 'text-right' }]}>
              <tr className="hover:bg-surface-2"><td className={tdc}>แถวหนึ่ง</td><td className={tdc}><Badge tone="ok" dot>ปกติ</Badge></td><td className={cn(tdc, 'text-right tnum')}>120</td></tr>
              <tr className="hover:bg-surface-2"><td className={tdc}>แถวสอง</td><td className={tdc}><Badge tone="warn" dot>ช้า</Badge></td><td className={cn(tdc, 'text-right tnum')}>340</td></tr>
            </DataTable>
          </div>
        </Demo>

        <Demo title="สถานะว่างและกำลังโหลด">
          <div className="w-full"><EmptyState icon={<Search />} title="ยังไม่มีรายการ" hint="บอกว่าต้องทำอะไรต่อ ไม่ใช่แค่ไม่มีข้อมูล" action={<Button variant="primary">เพิ่มรายการ</Button>} /></div>
          <div className="flex w-full items-center gap-3"><Skeleton className="size-10 rounded-full" /><div className="flex-1 space-y-2"><Skeleton className="h-4 w-1/2" /><Skeleton className="h-3 w-3/4" /></div><Spinner /></div>
        </Demo>

        <Demo title="ลิ้นชัก ยืนยัน เมนู และ Toast">
          <Button onClick={() => setSheet(true)}>เปิด Sheet</Button>
          <Button variant="danger" onClick={() => setConfirm(true)}>เปิด Confirm</Button>
          <Menu items={[{ label: 'แก้ไข', onSelect: () => toast({ tone: 'info', title: 'เลือกแก้ไข' }) }, { label: 'ลบ', danger: true, divider: true, onSelect: () => toast({ tone: 'bad', title: 'เลือกลบ' }) }]} trigger={({ toggle }) => <Button onClick={toggle}>เปิดเมนู</Button>} />
          <Button onClick={() => toast({ tone: 'info', title: 'ข้อมูล', detail: 'รายละเอียดสั้นๆ' })}>Toast ข้อมูล</Button>
          <Button onClick={() => toast({ tone: 'ok', title: 'สำเร็จ' })}>Toast สำเร็จ</Button>
          <Button onClick={() => toast({ tone: 'warn', title: 'เตือน' })}>Toast เตือน</Button>
          <Button onClick={() => toast({ tone: 'bad', title: 'ผิดพลาด' })}>Toast ผิดพลาด</Button>
        </Demo>
      </div>

      <Sheet open={sheet} onClose={() => setSheet(false)} title="ตัวอย่าง Sheet" footer={<div className="flex justify-end gap-2"><Button variant="ghost" onClick={() => setSheet(false)}>ปิด</Button><Button variant="primary" onClick={() => setSheet(false)}>ตกลง</Button></div>}>
        <p className="text-sm text-muted">ลิ้นชักข้างใช้แทน modal เพื่อไม่บังหน้าหลัก</p>
      </Sheet>
      <Confirm open={confirm} title="ลบรายการนี้?" detail="ลบแล้วกู้คืนไม่ได้" confirmLabel="ลบ" onCancel={() => setConfirm(false)} onConfirm={() => { setConfirm(false); toast({ tone: 'ok', title: 'ลบแล้ว' }) }} />
    </div>
  )
}
