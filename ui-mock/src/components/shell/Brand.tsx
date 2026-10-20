import { useApp } from '@/lib/store'

/** โลโก้ Studio Live — ภาพ webp ย่อขนาดแล้ว ล็อกสัดส่วนไว้ไม่ให้ layout ขยับ */
export function Brand({ compact }: { compact?: boolean }) {
  const theme = useApp((s) => s.theme)
  return (
    <div className="flex items-center gap-2.5">
      <img src="/brand/mark.webp" width={40} height={22} alt="" className="h-[22px] w-10 shrink-0" />
      {!compact && (
        <img
          src={theme === 'dark' ? '/brand/wordmark-dark.webp' : '/brand/wordmark-light.webp'}
          width={90} height={24} alt="Studio Live · Flow Ultra Tech" className="h-6 w-[90px]"
        />
      )}
    </div>
  )
}
