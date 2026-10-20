import type {
  AppNotification, AppSettings, AutoReplyConfig, AutomationRule, BotProxy, CaptchaRecord,
  Instance, LiveEvent, LogEntry, NetworkEntry, OverlayConfig, ProxyItem, UserLicense, WindowHandle,
} from './types'

const now = () => Date.now()
const mins = (n: number) => n * 60_000

export const defaultAutoReply = (): AutoReplyConfig => ({
  enabled: true,
  loopEnabled: false,
  loopIntervalSeconds: 90,
  loopRandom: false,
  loopMessages:
    'กดตะกร้าเหลืองมุมซ้ายได้เลยนะคะ\nสั่งวันนี้ส่งฟรีทั่วไทย\nถามไซส์ พิมพ์ไซส์ในแชทได้เลยค่ะ',
  rulesEnabled: true,
  rulesText:
    '# คำ1, คำ2 = คำตอบ1 | คำตอบ2\nราคา, เท่าไหร่ = {nickname} ราคาอยู่ในตะกร้าเหลืองนะคะ | ดูราคาที่ตะกร้าได้เลยค่ะ {nickname}\nส่งฟรี, ค่าส่ง = ส่งฟรีทั่วไทยค่ะ {nickname}\nไซส์ = ไซส์ S–XL ค่ะ บอกส่วนสูงน้ำหนักได้เลย',
  rulePrefixMention: true,
  greetEnabled: false,
  greetFrequency: 'once',
  greetMode: 'random',
  greetMinInterval: 8,
  greetMessages: 'ยินดีต้อนรับคุณ {nickname} เข้าห้องไลฟ์ค่ะ\nสวัสดีค่ะ {nickname} แวะดูโปรวันนี้ได้เลยนะคะ',
  aiEnabled: false,
  aiMode: 'reply',
  aiFilter: 'questions',
  aiModel: 'openai/gpt-4o-mini',
  aiBaseUrl: 'https://openrouter.ai/api/v1',
  aiKeySet: false,
  aiPrompt: 'คุณคือแอดมินร้านเสื้อผ้า ตอบสั้น เป็นกันเอง ราคาและโปรดูที่ตะกร้าเหลือง',
  aiForbidden: 'ฟรีทุกชิ้น, รับประกันคืนเงิน',
  aiMinWords: 6,
  aiMaxWords: 22,
  aiUserCooldown: 45,
  minIntervalSeconds: 4,
  userCooldownSeconds: 30,
  maxPerMinute: 12,
})

export const defaultOverlay = (): OverlayConfig => ({
  enabled: true,
  clockMode: 'wall',
  background: 'transparent',
  use24h: true,
  showSeconds: true,
  showThaiDate: true,
  primaryColor: '#F8FAFC',
  accentColor: '#EF4444',
  brandText: 'LIVE STUDIO',
  subtitleText: 'REALTIME VERIFIED',
  glare: true,
  shadow: true,
  jitter: true,
  ticker: true,
  flashDeal: false,
  flashTitle: 'โปรนาทีทอง ส่งฟรี',
  flashSeconds: 300,
})

const noProxy = (): BotProxy => ({
  enabled: false, protocol: 'Socks5', host: '', port: 1080, username: '', password: '',
})

export const seedInstances = (): Instance[] => [
  {
    id: 'acc01', name: 'Account 01', folder: 'Acc01', cdpPort: 9221, nickname: 'Mali Fashion', handle: 'mali.fashion',
    hue: 330, loggedIn: true, status: 'running', windowHidden: true, profileMode: 'direct', channel: 'mali.fashion',
    monitoring: true, proxyId: 'px1', botProxy: noProxy(), startedAt: now() - mins(94),
    stats: { viewers: 482, chats: 1284, gifts: 96, diamonds: 7420, likes: 18420, follows: 143, botReplies: 211 },
    autoReply: { ...defaultAutoReply(), loopEnabled: true }, overlay: defaultOverlay(), lease: { active: true, ttlMin: 8 },
  },
  {
    id: 'acc02', name: 'Account 02', folder: 'Acc02', cdpPort: 9222, nickname: 'Baan Ploy Shop', handle: 'baanploy.shop',
    hue: 160, loggedIn: true, status: 'running', windowHidden: false, profileMode: 'direct', channel: 'baanploy.shop',
    monitoring: true, proxyId: 'px2', botProxy: noProxy(), startedAt: now() - mins(41),
    captcha: { id: 'cp-live', kind: 'symbol', sessionId: 'a91f3c70e2', detectedAt: now() - 9_000, countdown: 38 },
    stats: { viewers: 217, chats: 640, gifts: 31, diamonds: 1960, likes: 6310, follows: 58, botReplies: 94 },
    autoReply: { ...defaultAutoReply(), greetEnabled: true }, overlay: { ...defaultOverlay(), clockMode: 'led' }, lease: { active: true, ttlMin: 9 },
  },
  {
    id: 'acc03', name: 'Account 03', folder: 'Acc03', cdpPort: 9223, nickname: 'Nong Gadget', handle: 'nonggadget',
    hue: 220, loggedIn: true, status: 'stopped', windowHidden: false, profileMode: 'direct', channel: 'nonggadget',
    monitoring: false, proxyId: 'px3',
    botProxy: { enabled: true, protocol: 'Http', host: 'bkk-edge.example.net', port: 8080, username: 'nong', password: 'secret-pass' },
    stats: { viewers: 0, chats: 0, gifts: 0, diamonds: 0, likes: 0, follows: 0, botReplies: 0 },
    autoReply: { ...defaultAutoReply(), aiEnabled: true, aiKeySet: true }, overlay: { ...defaultOverlay(), clockMode: 'digital' }, lease: { active: false, ttlMin: 0 },
  },
  {
    id: 'acc04', name: 'Account 04', folder: 'Acc04', cdpPort: 9224, nickname: 'Pet Planet TH', handle: 'petplanet.th',
    hue: 40, loggedIn: true, status: 'error', windowHidden: false, profileMode: 'default', channel: 'petplanet.th',
    monitoring: false, proxyId: null, botProxy: noProxy(),
    error: 'CDP ไม่ตอบสนองที่พอร์ต 9224 (หมดเวลา 25 วินาที) — ตรวจว่า Studio เปิดอยู่แล้วลองใหม่',
    stats: { viewers: 0, chats: 0, gifts: 0, diamonds: 0, likes: 0, follows: 0, botReplies: 0 },
    autoReply: defaultAutoReply(), overlay: defaultOverlay(), lease: { active: false, ttlMin: 0 },
  },
  {
    id: 'acc05', name: 'Account 05', folder: 'Acc05', cdpPort: 9225, nickname: '', handle: '',
    hue: 280, loggedIn: false, status: 'stopped', windowHidden: false, profileMode: 'direct', channel: '',
    monitoring: false, proxyId: null, botProxy: noProxy(),
    stats: { viewers: 0, chats: 0, gifts: 0, diamonds: 0, likes: 0, follows: 0, botReplies: 0 },
    autoReply: defaultAutoReply(), overlay: defaultOverlay(), lease: { active: false, ttlMin: 0 },
  },
]

export const seedProxies = (): ProxyItem[] => [
  { id: 'px1', name: 'BKK-01', protocol: 'Socks5', host: '203.0.113.21', port: 1080, username: 'flow01', password: 'pw-flow-01', enabled: true,
    test: { ok: true, ip: '203.0.113.21', pingMs: 38, at: now() - mins(6) } },
  { id: 'px2', name: 'BKK-02', protocol: 'Socks5', host: '203.0.113.22', port: 1080, username: 'flow02', password: 'pw-flow-02', enabled: true,
    test: { ok: true, ip: '203.0.113.22', pingMs: 52, at: now() - mins(6) } },
  { id: 'px3', name: 'CNX-01', protocol: 'Http', host: '198.51.100.40', port: 8080, username: '', password: '', enabled: true,
    test: { ok: false, ip: '', pingMs: 0, at: now() - mins(30) } },
  { id: 'px4', name: 'HKT-Spare', protocol: 'Https', host: '198.51.100.77', port: 443, username: 'spare', password: 'pw-spare', enabled: false },
]

export const seedRules = (): AutomationRule[] => [
  { id: 'r1', name: 'ถามราคา', trigger: 'ChatKeyword', pattern: 'ราคา|เท่าไหร่|กี่บาท', isRegex: true, minDiamonds: 0,
    reply: '@{Nickname} ราคาอยู่ในตะกร้าเหลืองค่ะ', cooldownSeconds: 5, enabled: true, hits: 148 },
  { id: 'r2', name: 'ขอบคุณของขวัญใหญ่', trigger: 'GiftMinDiamonds', pattern: '', isRegex: false, minDiamonds: 500, reply: 'ขอบคุณ {Nickname} สำหรับ {GiftName} x{Count} ค่ะ', cooldownSeconds: 3, enabled: true, hits: 12 },
  { id: 'r3', name: 'ทักคนเข้าห้อง', trigger: 'MemberJoin', pattern: '', isRegex: false, minDiamonds: 0, reply: 'ยินดีต้อนรับคุณ {Nickname} ค่ะ', cooldownSeconds: 10, enabled: false, hits: 0 },
  { id: 'r4', name: 'ไลก์ครบ 10,000', trigger: 'LikeMilestone', pattern: '10000', isRegex: false, minDiamonds: 0, reply: 'ไลก์ครบ {Likes} แล้ว ขอบคุณทุกคนค่ะ', cooldownSeconds: 600, enabled: true, hits: 1 },
]

export const seedHistory = (): CaptchaRecord[] => [
  { id: 'h1', instanceId: 'acc01', kind: 'jigsaw', at: now() - mins(14), result: 'solved', ms: 1840, mode: 'ai-auto' },
  { id: 'h2', instanceId: 'acc02', kind: 'symbol', at: now() - mins(33), result: 'solved', ms: 2310, mode: 'ai-auto' },
  { id: 'h3', instanceId: 'acc01', kind: 'symbol', at: now() - mins(58), result: 'failed', ms: 4020, mode: 'ai-auto' },
  { id: 'h4', instanceId: 'acc01', kind: 'jigsaw', at: now() - mins(71), result: 'solved', ms: 1620, mode: 'ai-manual' },
  { id: 'h5', instanceId: 'acc02', kind: 'inactivity', at: now() - mins(96), result: 'dismissed', ms: 0, mode: 'manual' },
  { id: 'h6', instanceId: 'acc01', kind: 'jigsaw', at: now() - mins(130), result: 'solved', ms: 1710, mode: 'ai-auto' },
]

export const seedNotifications = (): AppNotification[] => [
  { id: 'n1', at: now() - 9_000, tone: 'bad', title: 'พบ Symbol Captcha', detail: 'Baan Ploy Shop · รอแก้ 38 วินาที', instanceId: 'acc02', read: false },
  { id: 'n2', at: now() - mins(3), tone: 'warn', title: 'Account 04 เริ่มไม่สำเร็จ', detail: 'CDP ไม่ตอบสนอง', instanceId: 'acc04', read: false },
  { id: 'n3', at: now() - mins(14), tone: 'ok', title: 'แก้ Jigsaw สำเร็จ', detail: 'Mali Fashion · 1.8 วินาที', instanceId: 'acc01', read: true },
  { id: 'n4', at: now() - mins(60), tone: 'info', title: 'ต่อ Realtime แล้ว', detail: 'Lease ต่ออายุทุก 10 นาที', read: true },
]

export const seedLicense = (): UserLicense => ({
  username: 'operator.mali', role: 'user', plan: 'Pro', maxAccounts: 5, expiresAt: '2026-12-31',
  server: 'https://<server-host>:8001', hwid: '7F3A-91C2-D04B-58E1',
})

export const seedSettings = (): AppSettings => ({
  autoSolve: true, autoSolveDelay: 10, autoDetect: true, autoCopy: false, saveCaptchaImage: true,
  captchaFolder: 'C:\\Users\\Operator\\AppData\\Roaming\\FlowUltraTech\\Captchas',
  soundEnabled: true, soundLoop: true, soundVolume: 80, soundFile: '',
  solverEndpoint: 'https://<server-host>:8001',
  telegramEnabled: true, telegramToken: '7391820456:AAExampleExampleExampleExample', telegramChatId: '-1001928374650',
  telegramOnDetected: true, telegramSendPhoto: true, telegramOnSolved: true, telegramOnFailed: true,
  studioPath: 'C:\\Program Files\\TikTok LIVE Studio\\TikTok LIVE Studio.exe',
  proxifierPath: 'C:\\Program Files (x86)\\Proxifier\\Proxifier.exe',
  proxifierProfile: 'C:\\Users\\Operator\\AppData\\Roaming\\FlowUltraTech\\flow.ppx',
  autoHideOnLaunch: false, overlayPort: 19450,
})

/* ───────────── ตัวสร้างข้อมูลจำลองสำหรับแท็บเครื่องมือ ───────────── */

const NICKS = ['น้องมินท์', 'พี่บอล99', 'Fah_Fah', 'ลุงวิทย์', 'Jane.TH', 'ป้าแจ๋ว', 'โอ๊ตหล่อ', 'Aom🌷', 'ต้นกล้า', 'ปุ้ย ปุ้ย', 'KaoNiew', 'เจ๊หมวย']
const CHATS = ['ราคาเท่าไหร่คะ', 'ส่งฟรีไหม', 'มีไซส์ L ไหมคะ', 'สวยมากกก', 'ขอดูตัวสีครีมหน่อย', 'สั่งแล้วนะคะ', 'ส่งวันไหนคะ', 'ลดอีกได้ไหม', 'ฮัลโหลลล', 'เปิดตะกร้าหน่อยค่ะ', 'ของแท้ไหม', 'มีเก็บเงินปลายทางไหม']
const GIFTS: [string, number][] = [['Rose', 1], ['TikTok', 1], ['Finger Heart', 5], ['Doughnut', 30], ['Perfume', 20], ['Galaxy', 1000], ['Lion', 29999]]

let seq = 0
const uid = () => `e${Date.now().toString(36)}${(seq++).toString(36)}`
const pick = <T,>(a: T[]) => a[Math.floor(Math.random() * a.length)]

export function makeLiveEvent(): LiveEvent {
  const r = Math.random()
  const nickname = pick(NICKS)
  const username = nickname.toLowerCase().replace(/[^a-z0-9]/g, '') || 'user' + Math.floor(Math.random() * 999)
  const base = { id: uid(), at: Date.now(), nickname, username }
  if (r < 0.5) return { ...base, type: 'chat', message: pick(CHATS) }
  if (r < 0.68) {
    const [name, diamonds] = pick(GIFTS)
    const count = Math.ceil(Math.random() * 3)
    return { ...base, type: 'gift', message: `ส่ง ${name} x${count}`, gift: { name, diamonds, count } }
  }
  if (r < 0.8) return { ...base, type: 'like', message: `กดถูกใจ ${Math.ceil(Math.random() * 20)} ครั้ง` }
  if (r < 0.9) return { ...base, type: 'join', message: 'เข้าร่วมห้องไลฟ์' }
  if (r < 0.96) return { ...base, type: 'follow', message: 'ติดตามช่องแล้ว' }
  return { ...base, type: 'share', message: 'แชร์ไลฟ์ x1' }
}

export const seedLiveEvents = (n = 28): LiveEvent[] =>
  Array.from({ length: n }, (_, i) => ({ ...makeLiveEvent(), at: Date.now() - (n - i) * 4000 }))

export const seedLogs = (instanceName: string): LogEntry[] => {
  const t = now()
  const rows: [LogEntry['level'], string, string][] = [
    ['info', 'Process', `เปิด TikTok LIVE Studio (โปรไฟล์ ${instanceName})`],
    ['debug', 'Window', 'หา HWND จาก process tree: 0x0003A2C4 (Chrome_WidgetWin_1)'],
    ['info', 'Proxy', 'Native Bridge ฟังที่ 127.0.0.1:52144 → SOCKS5 BKK-01'],
    ['info', 'CDP', 'เชื่อมต่อพอร์ต 9221 สำเร็จ'],
    ['info', 'Lease', 'ขอ Slot Lease สำเร็จ (TTL 10 นาที)'],
    ['debug', 'Profile', 'อ่าน services.json: @mali.fashion'],
    ['info', 'Chat', 'เริ่มมอนิเตอร์ช่อง @mali.fashion (TikTokLive)'],
    ['warn', 'Network', 'webcast/room/enter ตอบช้า 2.4 วินาที'],
    ['info', 'Chat', 'Auto Reply: ตอบ @น้องมินท์ (โหมดกฎคำ)'],
    ['error', 'Captcha', 'Solver ตอบ 502 — ลองใหม่ครั้งที่ 1'],
    ['info', 'Captcha', 'แก้ Jigsaw สำเร็จ 1,840 ms'],
    ['info', 'Lease', 'ต่ออายุ Lease (เหลือ 9 นาที)'],
  ]
  return rows.map(([level, category, message], i) => ({ id: uid(), at: t - (rows.length - i) * 37_000, level, category, message }))
}

export const seedNetwork = (): NetworkEntry[] => {
  const t = now()
  const rows: [string, number, string, string, number][] = [
    ['GET', 200, 'fetch', 'https://webcast.tiktok.com/webcast/room/enter/?aid=1988', 412],
    ['POST', 200, 'xhr', 'https://webcast.tiktok.com/webcast/room/chat/', 188],
    ['GET', 200, 'ws', 'wss://webcast-ws.tiktok.com/webcast/im/ws_proxy/', 0],
    ['GET', 304, 'script', 'https://lf16-tiktok-web.example/obj/studio/main.js', 24],
    ['POST', 429, 'xhr', 'https://webcast.tiktok.com/webcast/room/chat/', 96],
    ['GET', 200, 'image', 'https://p16-sign.example/avatar/mali.jpeg', 61],
    ['GET', 200, 'fetch', 'https://mssdk.tiktok.com/web/report', 140],
  ]
  return rows.map(([method, status, type, url, ms], i) => ({
    id: uid(), at: t - (rows.length - i) * 8000, method, status, type, url, ms,
    body: JSON.stringify(status === 429 ? { status_code: 20003, message: 'rate limited' } : { status_code: 0, data: { room_id: '7391820456123', ok: true } }, null, 2),
  }))
}

export const seedHandles = (): WindowHandle[] => [
  { hwnd: '0x0003A2C4', role: 'Main Studio', size: '1280×800', title: 'TikTok LIVE Studio', visible: true },
  { hwnd: '0x000A19F0', role: 'WebView', size: '480×360', title: 'TikTok - Make Your Day', visible: false },
  { hwnd: '0x00041B7E', role: 'GPU Helper', size: '1×1', title: 'Chrome_WidgetWin_0', visible: false },
  { hwnd: '0x0008C512', role: 'Captcha Popup', size: '420×520', title: 'Verify to continue', visible: false },
]

