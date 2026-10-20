// โมเดลข้อมูลของ mock — อิงตาม StudioInstanceProfile / AppSettings / AutomationRule / InstanceAutoReplyConfig ของแอปจริง

export type InstanceStatus = 'stopped' | 'starting' | 'running' | 'error'
export type ProfileMode = 'direct' | 'default'
export type ProxyProtocol = 'Socks5' | 'Http' | 'Https'
export type CaptchaKind = 'jigsaw' | 'symbol' | 'inactivity'
export type LiveEventType = 'chat' | 'gift' | 'like' | 'follow' | 'share' | 'join' | 'bot'

export interface CaptchaAlert {
  id: string
  kind: CaptchaKind
  sessionId: string
  detectedAt: number
  /** วินาทีที่เหลือก่อนหมดเวลา */
  countdown: number
}

export interface CaptchaRecord {
  id: string
  instanceId: string
  kind: CaptchaKind
  at: number
  result: 'solved' | 'failed' | 'dismissed'
  ms: number
  mode: 'ai-auto' | 'ai-manual' | 'manual'
}

export interface LiveStats {
  viewers: number
  chats: number
  gifts: number
  diamonds: number
  likes: number
  follows: number
  botReplies: number
}

export interface AutoReplyConfig {
  enabled: boolean
  // โหมด 2 วนส่ง
  loopEnabled: boolean
  loopIntervalSeconds: number
  loopRandom: boolean
  loopMessages: string
  // โหมด 3 กฎคำ
  rulesEnabled: boolean
  rulesText: string
  rulePrefixMention: boolean
  // โหมด 4 ทักทาย
  greetEnabled: boolean
  greetFrequency: 'once' | 'every'
  greetMode: 'random' | 'ai'
  greetMinInterval: number
  greetMessages: string
  // โหมด 5 AI
  aiEnabled: boolean
  aiMode: 'reply' | 'periodic'
  aiFilter: 'all' | 'questions'
  aiModel: string
  aiBaseUrl: string
  aiKeySet: boolean
  aiPrompt: string
  aiForbidden: string
  aiMinWords: number
  aiMaxWords: number
  aiUserCooldown: number
  // limiter
  minIntervalSeconds: number
  userCooldownSeconds: number
  maxPerMinute: number
}

export type ClockMode = 'wall' | 'classic' | 'desk3d' | 'twinbell' | 'led' | 'digital'

export interface OverlayConfig {
  enabled: boolean
  clockMode: ClockMode
  background: 'transparent' | 'glass-dark' | 'glass-light'
  use24h: boolean
  showSeconds: boolean
  showThaiDate: boolean
  primaryColor: string
  accentColor: string
  brandText: string
  subtitleText: string
  glare: boolean
  shadow: boolean
  jitter: boolean
  ticker: boolean
  flashDeal: boolean
  flashTitle: string
  flashSeconds: number
  /** เวลาสิ้นสุดนาทีทอง (epoch ms) — ว่างเมื่อไม่ได้นับถอยหลัง */
  flashEndsAt?: number
}

export interface BotProxy {
  enabled: boolean
  protocol: ProxyProtocol
  host: string
  port: number
  username: string
  password: string
}

export interface Instance {
  id: string
  name: string
  folder: string
  cdpPort: number
  nickname: string
  handle: string
  hue: number
  loggedIn: boolean
  status: InstanceStatus
  windowHidden: boolean
  profileMode: ProfileMode
  channel: string
  monitoring: boolean
  proxyId: string | null
  botProxy: BotProxy
  error?: string
  captcha?: CaptchaAlert
  stats: LiveStats
  autoReply: AutoReplyConfig
  overlay: OverlayConfig
  startedAt?: number
  lease: { active: boolean; ttlMin: number }
}

export interface ProxyItem {
  id: string
  name: string
  protocol: ProxyProtocol
  host: string
  port: number
  username: string
  password: string
  enabled: boolean
  /** ผลทดสอบล่าสุด */
  test?: { ok: boolean; ip: string; pingMs: number; at: number } | 'testing'
}

export type RuleTrigger = 'ChatKeyword' | 'GiftMinDiamonds' | 'MemberJoin' | 'LikeMilestone'

export interface AutomationRule {
  id: string
  name: string
  trigger: RuleTrigger
  pattern: string
  isRegex: boolean
  minDiamonds: number
  reply: string
  cooldownSeconds: number
  enabled: boolean
  hits: number
}

export interface LiveEvent {
  id: string
  at: number
  type: LiveEventType
  nickname: string
  username: string
  message: string
  gift?: { name: string; diamonds: number; count: number }
  replied?: string
}

export interface LogEntry {
  id: string
  at: number
  level: 'info' | 'warn' | 'error' | 'debug'
  category: string
  message: string
}

export interface NetworkEntry {
  id: string
  at: number
  method: string
  status: number
  type: string
  url: string
  ms: number
  body: string
}

export interface WindowHandle {
  hwnd: string
  role: string
  size: string
  title: string
  visible: boolean
}

export interface AppNotification {
  id: string
  at: number
  tone: 'info' | 'ok' | 'warn' | 'bad'
  title: string
  detail?: string
  instanceId?: string
  read: boolean
}

export interface Toast {
  id: string
  tone: 'info' | 'ok' | 'warn' | 'bad'
  title: string
  detail?: string
}

export interface UserLicense {
  username: string
  role: 'user' | 'admin'
  plan: string
  maxAccounts: number
  expiresAt: string
  server: string
  hwid: string
}

export interface AppSettings {
  autoSolve: boolean
  autoSolveDelay: number
  autoDetect: boolean
  autoCopy: boolean
  saveCaptchaImage: boolean
  captchaFolder: string
  soundEnabled: boolean
  soundLoop: boolean
  soundVolume: number
  soundFile: string
  solverEndpoint: string
  telegramEnabled: boolean
  telegramToken: string
  telegramChatId: string
  telegramOnDetected: boolean
  telegramSendPhoto: boolean
  telegramOnSolved: boolean
  telegramOnFailed: boolean
  studioPath: string
  proxifierPath: string
  proxifierProfile: string
  autoHideOnLaunch: boolean
  overlayPort: number
}
