import { useEffect, useRef, useState } from 'react'

const seen = new Set<string>()

/** โหลดครั้งแรกของหน้า (จำลอง) — แสดง skeleton เฉพาะครั้งแรกใน session นี้ */
export function useFirstLoad(key: string, ms = 420) {
  const [loading, setLoading] = useState(() => !seen.has(key))
  useEffect(() => {
    if (!loading) return
    const t = window.setTimeout(() => { seen.add(key); setLoading(false) }, ms)
    return () => window.clearTimeout(t)
  }, [loading, key, ms])
  return loading
}

/**
 * ไฮไลต์หัวข้อตามส่วนที่เลื่อนถึง — คืน [activeId, jumpTo]
 * jumpTo เลื่อนไปที่ส่วนและล็อก active ชั่วคราวไม่ให้กระพริบระหว่างเลื่อน
 */
export function useScrollSpy(ids: string[], rootRef: React.RefObject<HTMLElement | null>, initial?: string) {
  const [active, setActive] = useState(initial && ids.includes(initial) ? initial : ids[0])
  const lock = useRef(0)
  const key = ids.join('|')

  useEffect(() => {
    const root = rootRef.current
    if (!root) return
    const els = ids.map((id) => root.querySelector<HTMLElement>(`#${CSS.escape(id)}`)).filter(Boolean) as HTMLElement[]
    if (!els.length) return
    const onScroll = () => {
      if (Date.now() < lock.current) return
      const top = root.getBoundingClientRect().top + 24
      let cur = els[0].id
      for (const el of els) if (el.getBoundingClientRect().top <= top) cur = el.id
      if (root.scrollTop + root.clientHeight >= root.scrollHeight - 4) cur = els[els.length - 1].id
      setActive(cur)
    }
    root.addEventListener('scroll', onScroll, { passive: true })
    return () => root.removeEventListener('scroll', onScroll)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, rootRef])

  const jumpTo = (id: string, smooth = true) => {
    const root = rootRef.current
    const el = root?.querySelector<HTMLElement>(`#${CSS.escape(id)}`)
    if (!root || !el) return
    lock.current = Date.now() + 700
    setActive(id)
    const top = root.scrollTop + el.getBoundingClientRect().top - root.getBoundingClientRect().top
    root.scrollTo({ top, behavior: smooth && !window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'smooth' : 'auto' })
  }
  return [active, jumpTo] as const
}
