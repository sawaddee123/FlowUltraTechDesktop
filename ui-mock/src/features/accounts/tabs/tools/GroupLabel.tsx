import type { ReactNode } from 'react'
import { CircleHelp } from 'lucide-react'

/** ป้ายหัวข้อของกลุ่มควบคุม (ไม่ใช้ <label> เพื่อไม่ให้คลิกป้ายไปกดตัวเลือกแรกในกลุ่ม) */
export function GroupLabel({ children, hint }: { children: ReactNode; hint?: string }) {
  return (
    <p className="mb-1.5 flex items-center gap-1.5 text-[13px] font-medium text-muted">
      {children}
      {hint && (
        <span className="group/h relative inline-flex">
          <CircleHelp className="size-3.5 text-subtle" aria-hidden />
          <span role="tooltip" className="pointer-events-none absolute bottom-full left-1/2 z-50 mb-1.5 w-60 -translate-x-1/2 rounded-md bg-fg px-2.5 py-1.5 text-xs font-normal text-bg opacity-0 shadow-float transition-opacity group-hover/h:opacity-100">
            {hint}
          </span>
        </span>
      )}
    </p>
  )
}
