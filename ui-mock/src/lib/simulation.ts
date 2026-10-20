import { useEffect, useRef } from 'react'
import { useApp } from './store'

/** จำลองชีพจรของระบบ: นับถอยหลัง Captcha, Auto Solve, สถิติไลฟ์ และอายุ Lease
 *  อัปเดตเฉพาะอินสแตนซ์ที่เปลี่ยนจริง เพื่อให้แถวอื่น (React.memo) ไม่เรนเดอร์ซ้ำ */
export function useSimulation() {
  const scheduled = useRef(new Set<string>())

  useEffect(() => {
    const tick = window.setInterval(() => {
      const s = useApp.getState()
      for (const inst of s.instances) {
        const cap = inst.captcha
        if (!cap) continue
        const left = cap.countdown - 1
        if (left <= 0) {
          s.patchInstance(inst.id, { captcha: undefined })
          s.push({ tone: 'warn', title: 'Captcha หมดเวลา', detail: inst.nickname || inst.name, instanceId: inst.id }, true)
          continue
        }
        s.patchInstance(inst.id, { captcha: { ...cap, countdown: left } })
        if (s.settings.autoSolve && !scheduled.current.has(cap.id)) {
          scheduled.current.add(cap.id)
          window.setTimeout(() => useApp.getState().solveCaptcha(inst.id, 'ai-auto'), s.settings.autoSolveDelay * 1000)
        }
      }
    }, 1000)

    const stats = window.setInterval(() => {
      const s = useApp.getState()
      for (const inst of s.instances) {
        if (inst.status !== 'running') continue
        const st = inst.stats
        const gift = Math.random() < 0.15
        s.patchInstance(inst.id, {
          stats: {
            ...st,
            viewers: Math.max(10, st.viewers + Math.round((Math.random() - 0.45) * 12)),
            chats: st.chats + Math.round(Math.random() * 4),
            likes: st.likes + Math.round(Math.random() * 40),
            gifts: st.gifts + (gift ? 1 : 0),
            diamonds: st.diamonds + (gift ? 5 + Math.round(Math.random() * 60) : 0),
            follows: st.follows + (Math.random() < 0.1 ? 1 : 0),
          },
        })
      }
    }, 3000)

    return () => { window.clearInterval(tick); window.clearInterval(stats) }
  }, [])
}
