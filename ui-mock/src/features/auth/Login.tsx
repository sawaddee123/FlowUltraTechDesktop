import { useEffect, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { ChevronDown, CircleAlert, Fingerprint, ShieldCheck } from 'lucide-react'
import { useApp } from '@/lib/store'
import { Button, Spinner } from '@/components/ui/base'
import { Field, Input } from '@/components/ui/form'
import { Brand } from '@/components/shell/Brand'

export default function Login() {
  const nav = useNavigate()
  const from = (useLocation().state as { from?: string } | null)?.from ?? '/accounts'
  const login = useApp((s) => s.login)
  const session = useApp((s) => s.session)
  const license = useApp((s) => s.license)
  const [user, setUser] = useState('')
  const [pass, setPass] = useState('')
  const [server, setServer] = useState(license.server)
  const [adv, setAdv] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  // mock: ถ้ามี token เดิม ตรวจสิทธิ์อัตโนมัติสั้นๆ
  const [checking, setChecking] = useState(session === 'checking')

  useEffect(() => {
    if (!checking) return
    const t = window.setTimeout(() => { login(); nav(from, { replace: true }) }, 1400)
    return () => window.clearTimeout(t)
  }, [checking, login, nav, from])

  const submit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!user.trim() || !pass) { setError('กรอกชื่อผู้ใช้และรหัสผ่าน'); return }
    setError(''); setBusy(true)
    window.setTimeout(() => {
      if (pass === 'wrong') { setBusy(false); setError('ชื่อผู้ใช้หรือรหัสผ่านไม่ถูกต้อง'); return }
      login(); nav(from, { replace: true })
    }, 1100)
  }

  return (
    <div className="grid h-full place-items-center px-6">
      <div className="w-full max-w-[380px] animate-rise">
        <div className="mb-10"><Brand /></div>

        {checking ? (
          <div className="flex flex-col gap-4" aria-live="polite">
            <div className="flex items-center gap-3 text-sm text-muted"><Spinner className="text-primary" />ตรวจสอบสิทธิ์</div>
            <div className="h-1 overflow-hidden rounded-full bg-surface-2"><div className="h-full w-1/3 animate-pulse-soft rounded-full bg-primary" /></div>
            <Button variant="ghost" className="self-start" onClick={() => setChecking(false)}>เข้าด้วยบัญชีอื่น</Button>
          </div>
        ) : (
          <form onSubmit={submit} className="flex flex-col gap-4" noValidate>
            <div>
              <h1 className="text-2xl font-semibold tracking-tight">เข้าสู่ระบบ</h1>
              <p className="mt-1 text-sm text-subtle">ใช้บัญชีที่ผู้ดูแลออกให้</p>
            </div>
            <Field label="ชื่อผู้ใช้">
              <Input value={user} onChange={(e) => setUser(e.target.value)} autoComplete="username" autoFocus aria-invalid={!!error && !user} />
            </Field>
            <Field label="รหัสผ่าน">
              <Input type="password" value={pass} onChange={(e) => setPass(e.target.value)} autoComplete="current-password" aria-invalid={!!error && !pass} />
            </Field>

            {error && (
              <p role="alert" className="flex items-center gap-2 rounded-ctl bg-bad-soft px-3 py-2 text-sm text-bad"><CircleAlert className="size-4 shrink-0" aria-hidden />{error}</p>
            )}

            <Button variant="primary" type="submit" loading={busy} className="h-10 w-full">เข้าสู่ระบบ</Button>

            <button type="button" onClick={() => setAdv((a) => !a)} aria-expanded={adv} className="flex items-center gap-1 self-start text-[13px] text-subtle hover:text-fg">
              ขั้นสูง <ChevronDown className={`size-3.5 transition-transform ${adv ? 'rotate-180' : ''}`} aria-hidden />
            </button>
            {adv && (
              <div className="flex animate-fade-in items-end gap-2">
                <Field label="Server" className="flex-1"><Input mono value={server} onChange={(e) => setServer(e.target.value)} /></Field>
                <Button variant="ghost" onClick={() => setServer(license.server)}>รีเซ็ต</Button>
              </div>
            )}
          </form>
        )}

        <div className="mt-10 flex items-center justify-between text-xs text-subtle">
          <span className="flex items-center gap-1.5"><Fingerprint className="size-3.5" aria-hidden />เครื่อง <span className="font-mono">{license.hwid}</span></span>
          <span className="flex items-center gap-1.5"><ShieldCheck className="size-3.5" aria-hidden />v1.7.0</span>
        </div>
      </div>
    </div>
  )
}


