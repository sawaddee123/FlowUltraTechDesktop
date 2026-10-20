import { create } from 'zustand'
import type {
  AppNotification, AppSettings, AutomationRule, CaptchaKind, CaptchaRecord, Instance, ProxyItem, Toast, UserLicense,
} from './types'
import {
  defaultAutoReply, defaultOverlay, seedHistory, seedInstances, seedLicense, seedNotifications, seedProxies, seedRules, seedSettings,
} from './mock'

type Tone = 'info' | 'ok' | 'warn' | 'bad'
let counter = 100
const nid = (p: string) => `${p}${++counter}`

interface State {
  theme: 'dark' | 'light'
  session: 'checking' | 'out' | 'in'
  license: UserLicense
  instances: Instance[]
  proxies: ProxyItem[]
  rules: AutomationRule[]
  settings: AppSettings
  history: CaptchaRecord[]
  notifications: AppNotification[]
  toasts: Toast[]
  status: string
  server: { connected: boolean; latencyMs: number }
  update: { open: boolean; mandatory: boolean }
  paletteOpen: boolean

  setTheme: (t: 'dark' | 'light') => void
  login: () => void
  logout: () => void
  setPalette: (v: boolean) => void
  setStatus: (s: string) => void

  patchInstance: (id: string, patch: Partial<Instance>) => void
  startInstance: (id: string) => void
  stopInstance: (id: string) => void
  startAll: () => void
  stopAll: () => void
  setWindowHidden: (id: string, hidden: boolean) => void
  hideAll: (hidden: boolean) => void
  scanAll: () => void
  addInstance: () => string | null
  removeInstance: (id: string) => void
  openLogin: (id: string) => void

  simulateCaptcha: (id?: string, kind?: CaptchaKind) => void
  solveCaptcha: (id: string, mode: CaptchaRecord['mode']) => void
  dismissCaptcha: (id: string) => void

  addProxy: () => void
  patchProxy: (id: string, patch: Partial<ProxyItem>) => void
  removeProxy: (id: string) => void
  testProxy: (id: string) => void

  addRule: () => string
  patchRule: (id: string, patch: Partial<AutomationRule>) => void
  removeRule: (id: string) => void

  saveSettings: (s: AppSettings) => void
  push: (n: Omit<AppNotification, 'id' | 'at' | 'read'>, toast?: boolean) => void
  markAllRead: () => void
  clearNotifications: () => void
  toast: (t: Omit<Toast, 'id'>) => void
  dismissToast: (id: string) => void
  setUpdate: (u: { open: boolean; mandatory: boolean }) => void
}

export const useApp = create<State>((set, get) => ({
  theme: (document.documentElement.dataset.theme as 'dark' | 'light') || 'dark',
  session: 'checking',
  license: seedLicense(),
  instances: seedInstances(),
  proxies: seedProxies(),
  rules: seedRules(),
  settings: seedSettings(),
  history: seedHistory(),
  notifications: seedNotifications(),
  toasts: [],
  status: 'พร้อมใช้งาน',
  server: { connected: true, latencyMs: 46 },
  update: { open: false, mandatory: false },
  paletteOpen: false,

  setTheme: (theme) => {
    document.documentElement.dataset.theme = theme
    try { localStorage.setItem('fut-theme', theme) } catch { /* ไม่มี storage */ }
    set({ theme })
  },
  login: () => set({ session: 'in' }),
  logout: () => set({ session: 'out' }),
  setPalette: (paletteOpen) => set({ paletteOpen }),
  setStatus: (status) => set({ status }),

  patchInstance: (id, patch) =>
    set((s) => ({ instances: s.instances.map((i) => (i.id === id ? { ...i, ...patch } : i)) })),

  startInstance: (id) => {
    const s = get()
    const inst = s.instances.find((i) => i.id === id)
    if (!inst || inst.status === 'running' || inst.status === 'starting') return
    const running = s.instances.filter((i) => i.status === 'running' || i.status === 'starting').length
    if (running >= s.license.maxAccounts) {
      s.toast({ tone: 'warn', title: 'เปิดครบโควต้าแล้ว', detail: `${running}/${s.license.maxAccounts} จอ` })
      return
    }
    s.patchInstance(id, { status: 'starting', error: undefined })
    s.setStatus(`กำลังเริ่ม ${inst.name}…`)
    window.setTimeout(() => {
      const fail = inst.id === 'acc04' && Math.random() < 0.5
      if (fail) {
        get().patchInstance(id, { status: 'error', error: 'CDP ไม่ตอบสนองที่พอร์ต ' + inst.cdpPort + ' (หมดเวลา 25 วินาที)' })
        get().push({ tone: 'bad', title: `${inst.name} เริ่มไม่สำเร็จ`, detail: 'CDP ไม่ตอบสนอง', instanceId: id }, true)
      } else {
        get().patchInstance(id, { status: 'running', startedAt: Date.now(), lease: { active: true, ttlMin: 10 } })
        get().setStatus(`${inst.name} กำลังทำงาน`)
      }
    }, 1500)
  },
  stopInstance: (id) => {
    const inst = get().instances.find((i) => i.id === id)
    if (!inst) return
    get().patchInstance(id, { status: 'stopped', captcha: undefined, monitoring: false, lease: { active: false, ttlMin: 0 }, startedAt: undefined })
    get().setStatus(`หยุด ${inst.name} แล้ว`)
  },
  startAll: () => {
    const s = get()
    s.instances.filter((i) => i.loggedIn && i.status !== 'running' && i.status !== 'starting').forEach((i, k) =>
      window.setTimeout(() => get().startInstance(i.id), k * 350))
  },
  stopAll: () => {
    get().instances.forEach((i) => i.status !== 'stopped' && get().stopInstance(i.id))
    get().toast({ tone: 'info', title: 'ปิดทุกจอแล้ว' })
  },
  setWindowHidden: (id, windowHidden) => get().patchInstance(id, { windowHidden }),
  hideAll: (hidden) =>
    set((s) => ({ instances: s.instances.map((i) => (i.status === 'running' || i.status === 'starting' ? { ...i, windowHidden: hidden } : i)) })),
  scanAll: () => {
    const n = get().instances.filter((i) => i.status === 'running').length
    get().toast({ tone: 'info', title: `สแกน Captcha ${n} บัญชี`, detail: n ? 'พบ 1 รายการรอแก้' : 'ไม่มีบัญชีที่ทำงานอยู่' })
  },
  addInstance: () => {
    const s = get()
    const n = s.instances.length + 1
    const num = String(n).padStart(2, '0')
    const id = `acc${num}`
    const inst: Instance = {
      id, name: `Account ${num}`, folder: `Acc${num}`, cdpPort: 9220 + n, nickname: '', handle: '', hue: (n * 67) % 360,
      loggedIn: false, status: 'stopped', windowHidden: false, profileMode: 'direct', channel: '', monitoring: false,
      proxyId: null, botProxy: { enabled: false, protocol: 'Socks5', host: '', port: 1080, username: '', password: '' },
      stats: { viewers: 0, chats: 0, gifts: 0, diamonds: 0, likes: 0, follows: 0, botReplies: 0 },
      autoReply: defaultAutoReply(), overlay: defaultOverlay(), lease: { active: false, ttlMin: 0 },
    }
    set({ instances: [...s.instances, inst] })
    s.toast({ tone: 'ok', title: `เพิ่ม ${inst.name}`, detail: `โฟลเดอร์ ${inst.folder} · CDP ${inst.cdpPort}` })
    return id
  },
  removeInstance: (id) => {
    const inst = get().instances.find((i) => i.id === id)
    set((s) => ({ instances: s.instances.filter((i) => i.id !== id) }))
    if (inst) get().toast({ tone: 'info', title: `ลบ ${inst.name} แล้ว` })
  },
  openLogin: (id) => {
    get().patchInstance(id, { status: 'starting' })
    get().setStatus('เปิด Studio เพื่อล็อกอิน… สแกน QR ในหน้าต่าง Studio')
    window.setTimeout(() => {
      get().patchInstance(id, { status: 'stopped', loggedIn: true, nickname: 'บัญชีใหม่', handle: 'new.account', channel: 'new.account' })
      get().push({ tone: 'ok', title: 'ล็อกอิน TikTok สำเร็จ', detail: 'ตรวจพบบัญชี @new.account — ปิด Studio ให้แล้ว', instanceId: id }, true)
    }, 3200)
  },

  simulateCaptcha: (id, kind = 'jigsaw') => {
    const s = get()
    const target = s.instances.find((i) => (id ? i.id === id : i.status === 'running' && !i.captcha))
    if (!target || target.status !== 'running') {
      s.toast({ tone: 'warn', title: 'ไม่มีบัญชีที่ทำงานอยู่', detail: 'เริ่มบัญชีก่อนจึงจำลอง Captcha ได้' })
      return
    }
    s.patchInstance(target.id, {
      captcha: { id: nid('cp'), kind, sessionId: Math.random().toString(16).slice(2, 12), detectedAt: Date.now(), countdown: 40 },
    })
    s.push({ tone: 'bad', title: `พบ ${kind === 'symbol' ? 'Symbol' : kind === 'jigsaw' ? 'Jigsaw' : 'Inactivity'} Captcha`, detail: `${target.nickname || target.name} · รอแก้ 40 วินาที`, instanceId: target.id }, true)
  },
  solveCaptcha: (id, mode) => {
    const inst = get().instances.find((i) => i.id === id)
    const cap = inst?.captcha
    if (!inst || !cap) return
    const ms = 1400 + Math.round(Math.random() * 1200)
    window.setTimeout(() => {
      const cur = get().instances.find((i) => i.id === id)
      if (!cur?.captcha || cur.captcha.id !== cap.id) return
      get().patchInstance(id, { captcha: undefined })
      set((s) => ({ history: [{ id: nid('h'), instanceId: id, kind: cap.kind, at: Date.now(), result: 'solved', ms, mode }, ...s.history] }))
      get().push({ tone: 'ok', title: 'แก้ Captcha สำเร็จ', detail: `${inst.nickname || inst.name} · ${(ms / 1000).toFixed(1)} วินาที`, instanceId: id }, true)
    }, ms)
  },
  dismissCaptcha: (id) => {
    const inst = get().instances.find((i) => i.id === id)
    if (!inst?.captcha) return
    const cap = inst.captcha
    get().patchInstance(id, { captcha: undefined })
    set((s) => ({ history: [{ id: nid('h'), instanceId: id, kind: cap.kind, at: Date.now(), result: 'dismissed', ms: 0, mode: 'manual' }, ...s.history] }))
  },

  addProxy: () =>
    set((s) => ({
      proxies: [...s.proxies, { id: nid('px'), name: `Proxy #${s.proxies.length + 1}`, protocol: 'Socks5', host: '', port: 1080, username: '', password: '', enabled: true }],
    })),
  patchProxy: (id, patch) => set((s) => ({ proxies: s.proxies.map((p) => (p.id === id ? { ...p, ...patch } : p)) })),
  removeProxy: (id) =>
    set((s) => ({
      proxies: s.proxies.filter((p) => p.id !== id),
      instances: s.instances.map((i) => (i.proxyId === id ? { ...i, proxyId: null } : i)),
    })),
  testProxy: (id) => {
    get().patchProxy(id, { test: 'testing' })
    window.setTimeout(() => {
      const p = get().proxies.find((x) => x.id === id)
      if (!p) return
      const ok = !!p.host && p.host !== '198.51.100.40'
      get().patchProxy(id, { test: { ok, ip: ok ? p.host : '', pingMs: ok ? 30 + Math.round(Math.random() * 60) : 0, at: Date.now() } })
    }, 1100)
  },

  addRule: () => {
    const id = nid('r')
    set((s) => ({
      rules: [...s.rules, { id, name: `กฎใหม่ #${s.rules.length + 1}`, trigger: 'ChatKeyword', pattern: '', isRegex: false, minDiamonds: 0, reply: '', cooldownSeconds: 3, enabled: true, hits: 0 }],
    }))
    return id
  },
  patchRule: (id, patch) => set((s) => ({ rules: s.rules.map((r) => (r.id === id ? { ...r, ...patch } : r)) })),
  removeRule: (id) => set((s) => ({ rules: s.rules.filter((r) => r.id !== id) })),

  saveSettings: (settings) => {
    set({ settings })
    get().toast({ tone: 'ok', title: 'บันทึกการตั้งค่าแล้ว', detail: 'ซิงก์ขึ้น Cloud สำเร็จ' })
  },
  push: (n, toast) => {
    const item: AppNotification = { ...n, id: nid('n'), at: Date.now(), read: false }
    set((s) => ({ notifications: [item, ...s.notifications].slice(0, 60) }))
    if (toast) get().toast({ tone: n.tone, title: n.title, detail: n.detail })
  },
  markAllRead: () => set((s) => ({ notifications: s.notifications.map((n) => ({ ...n, read: true })) })),
  clearNotifications: () => set({ notifications: [] }),
  toast: (t) => {
    const id = nid('t')
    set((s) => ({ toasts: [...s.toasts.slice(-3), { ...t, id }] }))
    window.setTimeout(() => get().dismissToast(id), 4200)
  },
  dismissToast: (id) => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })),
  setUpdate: (update) => set({ update }),
}))

export const selectRunning = (s: State) => s.instances.filter((i) => i.status === 'running' || i.status === 'starting').length
export type { Tone }
