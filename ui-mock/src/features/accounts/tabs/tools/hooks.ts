import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'

/** setTimeout ที่ถูกเคลียร์อัตโนมัติเมื่อ component ถูกถอด */
export function useTimers() {
  const ids = useRef<number[]>([])
  useEffect(() => () => ids.current.forEach((id) => window.clearTimeout(id)), [])
  return useCallback((fn: () => void, ms: number) => {
    const id = window.setTimeout(fn, ms)
    ids.current.push(id)
    return id
  }, [])
}

/** จำลองการโหลดข้อมูลครั้งแรก เพื่อให้เห็น Skeleton ที่ขนาดตรงของจริง */
export function useFakeLoad(ms = 420) {
  const [ready, setReady] = useState(false)
  useEffect(() => {
    const t = window.setTimeout(() => setReady(true), ms)
    return () => window.clearTimeout(t)
  }, [ms])
  return ready
}

/** ติดตามบรรทัดล่าสุดอัตโนมัติ หยุดเมื่อผู้ใช้เลื่อนขึ้น และมีปุ่มกระโดดกลับ */
export function useStickBottom<T extends HTMLElement>(dep: unknown) {
  const ref = useRef<T>(null)
  const stuckRef = useRef(true)
  const [stuck, setStuck] = useState(true)

  const onScroll = useCallback(() => {
    const el = ref.current
    if (!el) return
    const s = el.scrollHeight - el.scrollTop - el.clientHeight < 32
    if (s !== stuckRef.current) { stuckRef.current = s; setStuck(s) }
  }, [])

  useLayoutEffect(() => {
    const el = ref.current
    if (el && stuckRef.current) el.scrollTop = el.scrollHeight
  }, [dep])

  const jump = useCallback(() => {
    const el = ref.current
    if (!el) return
    el.scrollTop = el.scrollHeight
    stuckRef.current = true
    setStuck(true)
  }, [])

  return { ref, stuck, onScroll, jump }
}

export const pick = <T,>(a: readonly T[]) => a[Math.floor(Math.random() * a.length)]
