import { memo, useMemo } from 'react'
import { cn } from '@/lib/cn'
import type { CaptchaKind } from '@/lib/types'

/* ภาพจำลอง Captcha ของ TikTok ด้วย SVG (ไม่ใช้รูปภายนอก)
   สีในไฟล์นี้เป็นเนื้อหาจำลองของผลิตภัณฑ์อื่น จึงไม่ผูกโทเคนธีม — ส่วน marker ของ solver ใช้โทเคนของแอป
   ล็อกสัดส่วน 16:10 ทุกชนิด สลับชนิดแล้ว layout ไม่ขยับ */

const W = 320
const H = 200

const PV = {
  sky: '#cfe3f5', sky2: '#f4dcc0', sun: '#f6c95a', hillA: '#7aa879', hillB: '#4f8460', hillC: '#35614b',
  paper: '#f7f7f9', ink: '#20222b', mute: '#6b7080', line: '#d9dbe3', track: '#e8eaf0', knob: '#ffffff',
  studio: '#1a1b22', studio2: '#262832', card: '#ffffff', accent: '#fe2c55', cardInk: '#161823',
  shapes: ['#f08a3c', '#e2587a', '#8a5a3c', '#e9bf2f', '#58a55c', '#4a86d6', '#8a63c8', '#2fa6a0'],
}

function rng(seed: string) {
  let h = 1779033703 ^ seed.length
  for (let i = 0; i < seed.length; i++) { h = Math.imul(h ^ seed.charCodeAt(i), 3432918353); h = (h << 13) | (h >>> 19) }
  let a = h >>> 0
  return () => {
    a = (a + 0x6d2b79f5) >>> 0
    let t = a
    t = Math.imul(t ^ (t >>> 15), t | 1)
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61)
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

const poly = (n: number, r: number, rot = -Math.PI / 2, inner?: number) =>
  Array.from({ length: inner ? n * 2 : n }, (_, i) => {
    const rr = inner && i % 2 ? inner : r
    const a = rot + (i * Math.PI * (inner ? 1 : 2)) / n
    return `${(Math.cos(a) * rr).toFixed(1)},${(Math.sin(a) * rr).toFixed(1)}`
  }).join(' ')

/** รูปทรง 8 แบบ (วาดรอบจุด 0,0 ขนาดประมาณ 40×40) */
const SHAPES: ((fill: string) => React.ReactNode)[] = [
  (f) => <polygon points={poly(5, 20, -Math.PI / 2, 8.5)} fill={f} />,
  (f) => <path d="M0,17 C-24,0 -17,-19 0,-8 C17,-19 24,0 0,17Z" fill={f} />,
  (f) => <polygon points="0,-19 19,14 -19,14" fill={f} />,
  (f) => <polygon points={poly(6, 19)} fill={f} />,
  (f) => <polygon points="0,-20 16,0 0,20 -16,0" fill={f} />,
  (f) => <polygon points="-6,-19 6,-19 6,-6 19,-6 19,6 6,6 6,19 -6,19 -6,6 -19,6 -19,-6 -6,-6" fill={f} />,
  (f) => <rect x="-16" y="-16" width="32" height="32" rx="6" fill={f} />,
  (f) => <circle r="17" fill={f} />,
]

/** ลำดับรูปทรงที่ใช้จริง = สลับตาม seed */
function pickLayout(seed: string) {
  const r = rng(seed || 'x')
  const kinds = SHAPES.map((_, i) => i).sort(() => r() - 0.5)
  const pairKind = kinds[0]
  const cells = Array.from({ length: 8 }, (_, i) => i).sort(() => r() - 0.5)
  const [c1, c2] = cells
  const others = kinds.slice(1)
  let k = 0
  const items = Array.from({ length: 8 }, (_, cell) => {
    const isPair = cell === c1 || cell === c2
    const kind = isPair ? pairKind : others[k++ % others.length]
    return { cell, kind, color: PV.shapes[Math.floor(r() * PV.shapes.length)], rot: Math.round(r() * 80 - 40), dx: Math.round(r() * 10 - 5), dy: Math.round(r() * 8 - 4) }
  })
  return { items, pair: [c1, c2].sort((a, b) => a - b), pairKind, pairColor: items[c1].color }
}

const cellXY = (cell: number, dx = 0, dy = 0) => ({ x: 42 + (cell % 4) * 79 + dx, y: 58 + Math.floor(cell / 4) * 56 + dy })

/** ตำแหน่ง x ที่ solver คำนวณได้ของ Jigsaw (ขอบซ้ายของช่องว่าง) */
export const jigsawTarget = (seed: string) => 150 + Math.floor(rng(seed + 'j')() * 120)

const pieceD = (x: number, y: number, s = 44) =>
  `M${x},${y} h${s * 0.38} a${s * 0.12},${s * 0.12} 0 1 1 ${s * 0.24},0 h${s * 0.38} v${s * 0.38} a${s * 0.12},${s * 0.12} 0 1 1 0,${s * 0.24} v${s * 0.38} h-${s} Z`

function Jigsaw({ seed, marker }: { seed: string; marker: boolean }) {
  const tx = jigsawTarget(seed)
  const y = 54
  const track = { x: 14, w: 292 }
  const knobX = track.x + ((tx - 14) / (W - 44 - 14)) * (track.w - 34)
  return (
    <g>
      <defs><clipPath id={`jc-${seed}`}><rect width={W} height="148" /></clipPath></defs>
      <g clipPath={`url(#jc-${seed})`}>
        <rect width={W} height="148" fill={PV.sky} />
        <rect y="70" width={W} height="78" fill={PV.sky2} opacity="0.6" />
        <circle cx="248" cy="36" r="18" fill={PV.sun} />
        <path d="M0,110 C50,70 90,92 140,84 C200,74 250,100 320,76 V148 H0Z" fill={PV.hillA} />
        <path d="M0,128 C60,100 110,120 170,108 C230,96 280,122 320,110 V148 H0Z" fill={PV.hillB} />
        <path d="M0,142 C80,124 140,140 210,132 C260,126 300,138 320,134 V148 H0Z" fill={PV.hillC} />
        <path d={pieceD(tx, y)} fill="#0f1a24" opacity="0.52" />
        <path d={pieceD(tx, y)} fill="none" stroke="#ffffff" strokeOpacity="0.55" strokeWidth="1" />
        <path d={pieceD(14, y)} fill={PV.hillA} stroke="#ffffff" strokeWidth="1.6" />
        <circle cx="30" cy={y + 24} r="5" fill={PV.sun} opacity="0.9" />
      </g>
      {/* แถบลาก */}
      <rect y="148" width={W} height="52" fill={PV.paper} />
      <rect x={track.x} y="164" width={track.w} height="22" rx="11" fill={PV.track} />
      <text x={W / 2 + 10} y="179" textAnchor="middle" fontSize="9.5" fill={PV.mute} fontFamily="inherit">Drag the puzzle piece into place</text>
      <rect x={track.x} y="161" width="34" height="28" rx="9" fill={PV.knob} stroke={PV.line} />
      <path d={`M${track.x + 13},175 h8 m-3,-3.5 l3.5,3.5 l-3.5,3.5`} fill="none" stroke={PV.mute} strokeWidth="1.6" strokeLinecap="round" strokeLinejoin="round" />
      {marker && (
        <g className="animate-pulse-soft">
          <rect x={tx - 3} y={y - 3} width="50" height="50" rx="6" fill="none" stroke="#fff" strokeOpacity=".9" strokeWidth="3.5" strokeDasharray="5 3" />
          <rect x={tx - 3} y={y - 3} width="50" height="50" rx="6" fill="none" stroke="var(--primary)" strokeWidth="1.8" strokeDasharray="5 3" />
          <line x1={tx + 22} y1={y + 48} x2={tx + 22} y2="161" stroke="var(--primary)" strokeWidth="1.4" strokeDasharray="3 3" />
          <rect x={knobX} y="161" width="34" height="28" rx="9" fill="none" stroke="#fff" strokeOpacity=".9" strokeWidth="3.5" />
          <rect x={knobX} y="161" width="34" height="28" rx="9" fill="none" stroke="var(--primary)" strokeWidth="1.8" />
          <rect x={tx + 2} y={y - 22} width="62" height="16" rx="8" fill="var(--primary)" />
          <text x={tx + 33} y={y - 10.5} textAnchor="middle" fontSize="9.5" fontWeight="600" fill="var(--on-primary)" fontFamily="inherit">x = {tx}</text>
        </g>
      )}
    </g>
  )
}

function Symbol({ seed, marker }: { seed: string; marker: boolean }) {
  const lay = useMemo(() => pickLayout(seed), [seed])
  return (
    <g>
      <rect width={W} height={H} fill={PV.paper} />
      <text x="14" y="19" fontSize="10.5" fontWeight="600" fill={PV.ink} fontFamily="inherit">Select 2 objects that are the same shape</text>
      <rect x="8" y="28" width="304" height="124" rx="8" fill="#fff" stroke={PV.line} />
      {lay.items.map((it) => {
        const { x, y } = cellXY(it.cell, it.dx, it.dy)
        return <g key={it.cell} transform={`translate(${x} ${y + 4}) rotate(${it.rot})`}>{SHAPES[it.kind](it.color)}</g>
      })}
      {/* ภาพอ้างอิง 2 ชิ้น (รูปทรงที่ต้องหา) */}
      <text x="14" y="173" fontSize="9" fill={PV.mute} fontFamily="inherit">Target</text>
      {[0, 1].map((n) => (
        <g key={n}>
          <rect x={52 + n * 46} y="160" width="38" height="32" rx="7" fill="#fff" stroke={PV.line} />
          <g transform={`translate(${71 + n * 46} 176) scale(.62) rotate(${n ? 18 : -14})`}>{SHAPES[lay.pairKind](PV.ink)}</g>
        </g>
      ))}
      <rect x="226" y="161" width="86" height="30" rx="8" fill={PV.accent} />
      <text x="269" y="180" textAnchor="middle" fontSize="11" fontWeight="600" fill="#fff" fontFamily="inherit">Confirm</text>
      {marker && lay.pair.map((cell, n) => {
        const it = lay.items[cell]
        const { x, y } = cellXY(cell, it.dx, it.dy)
        return (
          <g key={cell} className="animate-pulse-soft">
            <circle cx={x} cy={y + 4} r="24" fill="none" stroke="#fff" strokeOpacity=".9" strokeWidth="4" />
            <circle cx={x} cy={y + 4} r="24" fill="none" stroke="var(--primary)" strokeWidth="2" />
            <circle cx={x + 18} cy={y - 16} r="9" fill="var(--primary)" />
            <text x={x + 18} y={y - 12.5} textAnchor="middle" fontSize="11" fontWeight="700" fill="var(--on-primary)" fontFamily="inherit">{n + 1}</text>
          </g>
        )
      })}
    </g>
  )
}

function Inactivity({ countdown }: { countdown: number }) {
  const mm = String(Math.floor(countdown / 60)).padStart(2, '0')
  const ss = String(countdown % 60).padStart(2, '0')
  return (
    <g>
      <rect width={W} height={H} fill={PV.studio} />
      <rect x="0" y="0" width={W} height="22" fill={PV.studio2} />
      <rect x="10" y="7" width="40" height="8" rx="4" fill="#3a3d4a" />
      <rect x="12" y="34" width="190" height="112" rx="6" fill={PV.studio2} />
      <rect x="212" y="34" width="96" height="112" rx="6" fill={PV.studio2} />
      <rect x="12" y="156" width="296" height="30" rx="6" fill={PV.studio2} />
      <rect width={W} height={H} fill="#000" opacity="0.52" />
      <rect x="40" y="36" width="240" height="128" rx="10" fill={PV.card} />
      <circle cx="160" cy="62" r="13" fill="#fff0e0" stroke="#f0a24a" strokeWidth="2" />
      <path d="M160,54 v9 l6,4" fill="none" stroke="#e08a2a" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" />
      <text x="160" y="92" textAnchor="middle" fontSize="10" fontWeight="600" fill={PV.cardInk} fontFamily="inherit">We detected inactivity during</text>
      <text x="160" y="104" textAnchor="middle" fontSize="10" fontWeight="600" fill={PV.cardInk} fontFamily="inherit">your current LIVE</text>
      <text x="160" y="124" textAnchor="middle" fontSize="11" fill={PV.mute} fontFamily="inherit">LIVE will end in: <tspan fontWeight="700" fill={PV.accent}>{mm}:{ss}</tspan></text>
      <rect x="58" y="136" width="98" height="20" rx="6" fill="#f1f1f4" />
      <text x="107" y="150" textAnchor="middle" fontSize="9.5" fontWeight="600" fill={PV.cardInk} fontFamily="inherit">End LIVE</text>
      <rect x="164" y="136" width="98" height="20" rx="6" fill={PV.accent} />
      <text x="213" y="150" textAnchor="middle" fontSize="9.5" fontWeight="600" fill="#fff" fontFamily="inherit">Continue LIVE</text>
    </g>
  )
}

const alt: Record<CaptchaKind, string> = {
  jigsaw: 'ภาพจำลอง Captcha แบบลากชิ้นส่วน (Jigsaw)',
  symbol: 'ภาพจำลอง Captcha แบบเลือกรูปทรงเหมือนกัน (Symbol)',
  inactivity: 'ภาพจำลองหน้าต่างเตือนไม่มีกิจกรรม (Inactivity)',
}

/** `showSolution` = แสดงตำแหน่งที่ solver คำนวณ (Jigsaw: ช่องว่าง+ปลายทางปุ่มลาก, Symbol: จุดคลิก 1/2) */
export const CaptchaPreview = memo(function CaptchaPreview({ kind, seed, countdown = 0, showSolution, className }: {
  kind: CaptchaKind; seed: string; countdown?: number; showSolution?: boolean; className?: string
}) {
  return (
    <div className={cn('relative aspect-[16/10] w-full overflow-hidden rounded-ctl bg-surface-2', className)}>
      <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label={alt[kind]} className="block size-full" preserveAspectRatio="xMidYMid slice">
        {kind === 'jigsaw' && <Jigsaw seed={seed} marker={!!showSolution} />}
        {kind === 'symbol' && <Symbol seed={seed} marker={!!showSolution} />}
        {kind === 'inactivity' && <Inactivity countdown={countdown} />}
      </svg>
    </div>
  )
})
