import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useNavigate } from 'react-router-dom'
import {
  CornerDownLeft, EyeOff, Eye, Globe, LogOut, MessagesSquare, Moon, Play, ScanSearch, Search, Settings2, Square, UserPlus, Users,
} from 'lucide-react'
import { useApp } from '@/lib/store'
import { displayName } from '@/lib/format'
import { Avatar } from '@/components/ui/base'
import { cn } from '@/lib/cn'

interface Cmd { id: string; group: string; label: string; icon: ReactNode; run: () => void; keywords?: string; trailing?: ReactNode }

/** ผู้ฟังคีย์ลัด + ตัวเปิด/ปิด — ตัวพาเลตต์จริงเมานต์เฉพาะตอนเปิด จึงไม่เรนเดอร์ตาม state ทุกวินาที */
export function CommandPalette() {
  const open = useApp((s) => s.paletteOpen)
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') { e.preventDefault(); useApp.getState().setPalette(!useApp.getState().paletteOpen) }
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [])
  return open ? <PaletteBody /> : null
}

function PaletteBody() {
  const setOpen = useApp((s) => s.setPalette)
  const nav = useNavigate()
  const [q, setQ] = useState('')
  const [idx, setIdx] = useState(0)
  const inputRef = useRef<HTMLInputElement>(null)
  const st = useApp()

  useEffect(() => { inputRef.current?.focus() }, [])

  const cmds = useMemo<Cmd[]>(() => {
    const go = (to: string) => () => nav(to)
    return [
      { id: 'n1', group: 'ไปที่', label: 'บัญชีสตูดิโอ', icon: <Users />, run: go('/accounts') },
      { id: 'n2', group: 'ไปที่', label: 'ศูนย์ Captcha', icon: <ScanSearch />, run: go('/captcha') },
      { id: 'n3', group: 'ไปที่', label: 'Proxy เครือข่าย', icon: <Globe />, run: go('/proxy') },
      { id: 'n4', group: 'ไปที่', label: 'กฎตอบกลับ', icon: <MessagesSquare />, run: go('/rules') },
      { id: 'n5', group: 'ไปที่', label: 'ตั้งค่า', icon: <Settings2 />, run: go('/settings') },
      ...st.instances.map<Cmd>((i) => ({
        id: 'i' + i.id, group: 'บัญชี', label: displayName(i), keywords: `${i.handle} ${i.name}`,
        icon: <Avatar name={displayName(i)} hue={i.hue} size={20} />, run: go(`/accounts/${i.id}`),
        trailing: <span className="text-xs text-subtle">{i.handle ? '@' + i.handle : i.name}</span>,
      })),
      { id: 'a1', group: 'คำสั่ง', label: 'เริ่มทุกจอ', icon: <Play />, run: st.startAll },
      { id: 'a2', group: 'คำสั่ง', label: 'ปิดทุกจอ', icon: <Square />, run: st.stopAll },
      { id: 'a3', group: 'คำสั่ง', label: 'ซ่อนทุกจอ', icon: <EyeOff />, run: () => st.hideAll(true) },
      { id: 'a4', group: 'คำสั่ง', label: 'แสดงทุกจอ', icon: <Eye />, run: () => st.hideAll(false) },
      { id: 'a5', group: 'คำสั่ง', label: 'สแกน Captcha ทุกบัญชี', icon: <ScanSearch />, run: st.scanAll },
      { id: 'a6', group: 'คำสั่ง', label: 'เพิ่มบัญชีใหม่', icon: <UserPlus />, run: () => { const id = st.addInstance(); if (id) nav(`/accounts/${id}`) } },
      { id: 'a7', group: 'คำสั่ง', label: st.theme === 'dark' ? 'สลับเป็นธีมสว่าง' : 'สลับเป็นธีมมืด', icon: <Moon />, run: () => st.setTheme(st.theme === 'dark' ? 'light' : 'dark') },
      { id: 'a8', group: 'คำสั่ง', label: 'ออกจากระบบ', icon: <LogOut />, run: () => { st.logout(); nav('/login') } },
    ]
  }, [st.instances, st.theme]) // eslint-disable-line react-hooks/exhaustive-deps

  const list = useMemo(() => {
    const k = q.trim().toLowerCase()
    return k ? cmds.filter((c) => `${c.label} ${c.keywords ?? ''} ${c.group}`.toLowerCase().includes(k)) : cmds
  }, [cmds, q])

  const choose = (c?: Cmd) => { if (!c) return; setOpen(false); c.run() }
  let lastGroup = ''

  return createPortal(
    <div className="fixed inset-0 z-[90] grid place-items-start justify-center pt-[14vh]">
      <div className="absolute inset-0 animate-fade-in" style={{ background: 'var(--scrim)' }} onClick={() => setOpen(false)} />
      <div role="dialog" aria-modal="true" aria-label="ค้นหาและสั่งงาน" className="relative w-[min(560px,92vw)] animate-rise overflow-hidden rounded-card bg-surface shadow-float hairline"
        onKeyDown={(e) => {
          if (e.key === 'Escape') setOpen(false)
          if (e.key === 'ArrowDown') { e.preventDefault(); setIdx((i) => Math.min(list.length - 1, i + 1)) }
          if (e.key === 'ArrowUp') { e.preventDefault(); setIdx((i) => Math.max(0, i - 1)) }
          if (e.key === 'Enter') choose(list[idx])
        }}>
        <div className="flex h-12 items-center gap-3 px-4">
          <Search className="size-4 text-subtle" aria-hidden />
          <input ref={inputRef} value={q} onChange={(e) => { setQ(e.target.value); setIdx(0) }} placeholder="พิมพ์ชื่อบัญชีหรือคำสั่ง" aria-label="ค้นหา"
            className="h-full flex-1 bg-transparent text-sm outline-none placeholder:text-subtle" />
        </div>
        <ul role="listbox" className="scroll-y max-h-[50vh] bg-surface-2/40 p-1.5">
          {list.length === 0 && <li className="px-3 py-8 text-center text-sm text-subtle">ไม่พบรายการ</li>}
          {list.map((c, i) => {
            const head = c.group !== lastGroup ? c.group : null
            lastGroup = c.group
            return (
              <li key={c.id} role="option" aria-selected={i === idx}>
                {head && <p className="px-2.5 pb-1 pt-2.5 text-xs font-medium text-subtle">{head}</p>}
                <button type="button" onMouseMove={() => setIdx(i)} onClick={() => choose(c)}
                  className={cn('flex h-10 w-full items-center gap-3 rounded-lg px-2.5 text-left text-sm', i === idx ? 'bg-surface text-fg shadow-sm' : 'text-muted')}>
                  <span className="grid size-5 place-items-center text-subtle [&>svg]:size-4">{c.icon}</span>
                  <span className="flex-1 truncate">{c.label}</span>
                  {c.trailing}
                  {i === idx && <CornerDownLeft className="size-3.5 text-subtle" aria-hidden />}
                </button>
              </li>
            )
          })}
        </ul>
      </div>
    </div>,
    document.body,
  )
}
