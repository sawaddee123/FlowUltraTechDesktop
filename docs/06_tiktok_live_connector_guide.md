# 06. TikTok-Live-Connector Integration Guide

คู่มือเชิงลึกและการนำ [zerodytrash/TikTok-Live-Connector](https://github.com/zerodytrash/TikTok-Live-Connector) มาประยุกต์ใช้งานร่วมกับสถาปัตยกรรม **TikTok LIVE Studio Multi-Instance Sandbox**

---

## 1. ภาพรวมและหลักการทำงาน (Overview & Architecture)

`TikTok-Live-Connector` เป็นไลบรารี Node.js แบบโอเพนซอร์สที่ทำงานโดยการทำ Reverse Engineering โพรโทคอล **Webcast Push Service** ของ TikTok โดยตรง

```mermaid
graph LR
    User["@username"] --> Resolve["Room ID Resolver (HTTP GET)"]
    Resolve --> WebcastWS["TikTok Webcast Server (WebSocket)"]
    WebcastWS -->|Protobuf Binary Stream| Connector["TikTok-Live-Connector"]
    Connector -->|Decode Protobuf| Events["Node.js EventEmitter\n(chat, gift, like, member...)"]
```

### จุดเด่นหลัก:
* **ไม่ต้องใช้รหัสผ่านหรือบัญชีทางการ (Zero-Auth):** ทำงานได้โดยระบุเพียงชื่อผู้ใช้ (`uniqueId` หรือ `@username`) ของห้องไลฟ์ที่ต้องการดักฟัง
* **ความหน่วงต่ำมาก (Real-time Low Latency):** รับอีเวนต์ผ่าน WebSocket และถอดรหัส binary ด้วย Protocol Buffers (Protobuf)
* **ทรัพยากรต่ำมาก:** ไม่ต้องรันเบราว์เซอร์หรือเรนเดอร์ UI ใดๆ สามารถดักฟังหลายสิบห้องไลฟ์พร้อมกันในโปรเซส Node.js เดียว

---

## 2. เหตุผลที่นำมาใช้ร่วมกับ Sandbox (Why Hybrid Architecture?)

ในโปรเจกต์นี้ เราควบคุม TikTok LIVE Studio ผ่าน **CDP (Chrome DevTools Protocol)** เพื่อสตรีมและส่งข้อความ แต่การนำ `TikTok-Live-Connector` มาเสริมเป็น **สถาปัตยกรรมแบบไฮบริด (Hybrid Architecture)** จะช่วยแก้ปัญหาคอขวดได้อย่างสมบูรณ์แบบ:

| มิติการทำงาน | สั่งการผ่าน CDP (TikTok LIVE Studio) | ใช้ TikTok-Live-Connector (Webcast) |
| :--- | :--- | :--- |
| **การดักจับอีเวนต์ (Reading Events)** | ต้อง Hook WebSocket หรือ Query DOM ภายใน Chromium สิ้นเปลือง CPU/RAM ต่อจอ | เบามาก รันแยกต่างหาก รับแชต/ของขวัญได้แบบไม่ต้องเปิดจอ |
| **การส่งข้อความ (Sending Chat)** | **ปลอดภัยสูงสุด (100% Legitimate Session)** ไม่โดนแบน ไม่ติด 403 Forbidden | มีโอกาสสูงมากที่จะถูก TikTok ปฏิเสธ (403 Forbidden / Anti-Bot Signature) |
| **ความเหมาะสม** | เหมาะสำหรับ: **Sender & Controller** (เปลี่ยนฉาก, สั่งส่งข้อความ, ปรับเสียง) | เหมาะสำหรับ: **Listener & Analytics** (ดักแชท, นับคะแนน, บอทคำนวณของขวัญ) |

### สถาปัตยกรรมผสมผสาน (Recommended Hybrid Pipeline):
```mermaid
graph TD
    subgraph TikTok Live Room
        Room["TikTok LIVE Stream (@mychannel)"]
    end

    subgraph Event Ingestion Layer (Lightweight)
        TLC["TikTok-Live-Connector (Node.js)"]
        Room -->|Webcast WebSocket| TLC
    end

    subgraph Business Logic & Bot Engine
        Bot["Chatbot / Reward / Game Logic Engine"]
        TLC -->|Emit: chat, gift, like| Bot
    end

    subgraph Studio Control Layer (Multi-Instance Sandbox)
        CDP["CDP Client (Port 9221)"]
        Studio["TikTok LIVE Studio (Hidden UI)"]
        Bot -->|Send Auto-Reply / Command| CDP
        CDP -->|In-Context Send Message / UI Trigger| Studio
        Studio -->|Official Stream Output| Room
    end
```

---

## 3. การติดตั้งและการเริ่มต้นใช้งาน (Getting Started)

### 3.1 การติดตั้งแพ็กเกจ
```bash
npm install tiktok-live-connector
# หากต้องการเชื่อมต่อผ่าน Proxy แนะนำให้ติดตั้ง https-proxy-agent ด้วย
npm install https-proxy-agent
```

### 3.2 ตัวอย่างโค้ดพื้นฐาน (JavaScript / TypeScript)
```javascript
const { TikTokLiveConnection } = require('tiktok-live-connector');

// กำหนด username ของช่องที่กำลังไลฟ์อยู่
const tiktokUsername = 'target_creator_username';

// สร้างอ็อบเจกต์ Connection
const tiktokLiveConnection = new TikTokLiveConnection(tiktokUsername, {
    processInitialData: false,      // ข้ามอีเวนต์เก่าก่อนที่เราจะเชื่อมต่อ
    enableExtendedGiftInfo: true,   // ดึงข้อมูลราคาเหรียญและชื่อของขวัญแบบละเอียด
    requestPollingIntervalMs: 1000  // Fallback Polling หาก WebSocket หลุด
});

// เชื่อมต่อเข้าห้องไลฟ์
tiktokLiveConnection.connect().then(state => {
    console.info(`[Connected] ห้องไลฟ์ Room ID: ${state.roomId}`);
}).catch(err => {
    console.error('[Connect Failed]:', err);
});

// ดักจับข้อความแชต (Chat Comments)
tiktokLiveConnection.on('chat', data => {
    console.log(`💬 [Chat] ${data.uniqueId} (${data.nickname}): ${data.comment}`);
});

// ดักจับของขวัญ (Gifts & Streaks)
tiktokLiveConnection.on('gift', data => {
    if (data.giftType === 1 && !data.repeatEnd) {
        // ของขวัญแบบส่งรัว (Streak กำลังนับ)
        console.log(`🎁 [Gift Streak] ${data.uniqueId} ส่ง ${data.giftName} x${data.repeatCount}`);
    } else {
        // ของขวัญแบบชิ้นเดียว หรือ Streak สิ้นสุดแล้ว
        console.log(`🎉 [Gift Final] ${data.uniqueId} ส่ง ${data.giftName} รวมทั้งหมด ${data.repeatCount} ชิ้น (${data.diamondCount * data.repeatCount} เหรียญ)`);
    }
});

// ดักจับการกดไลก์ (Likes)
tiktokLiveConnection.on('like', data => {
    console.log(`❤️ [Like] ${data.uniqueId} กดไลก์ ${data.likeCount} ครั้ง (ยอดรวมห้อง: ${data.totalLikeCount})`);
});

// ดักจับผู้ใช้เข้าห้อง (Member Join)
tiktokLiveConnection.on('member', data => {
    console.log(`👋 [Member Join] ${data.uniqueId} เข้ามาชมห้องไลฟ์`);
});

// ดักจับยอดผู้รับชมปัจจุบัน (Room Viewers)
tiktokLiveConnection.on('roomUser', data => {
    console.log(`👥 [Viewer Count] ผู้ชมปัจจุบัน: ${data.viewerCount} คน`);
});

// ดักจับการสิ้นสุดการไลฟ์
tiktokLiveConnection.on('streamEnd', actionId => {
    console.log(`🛑 [Stream Ended] สตรีมจบลงแล้ว (ActionId: ${actionId})`);
});

tiktokLiveConnection.on('error', err => {
    console.error('⚠️ [Error Event]:', err);
});
```

---

## 4. ตัวเลือกการตั้งค่าขั้นสูงและการเชื่อมต่อผ่าน Proxy (Advanced Options & Proxy)

เพื่อให้สอดคล้องกับโครงสร้าง **Network & Proxy Isolation** ของ Sandbox เราสามารถตั้งค่าให้ `TikTok-Live-Connector` วิ่งผ่าน Proxy ตัวเดียวกับที่ Instance นั้นๆ ใช้งานอยู่ได้:

```javascript
const { TikTokLiveConnection } = require('tiktok-live-connector');
const { HttpsProxyAgent } = require('https-proxy-agent');

// กำหนด Proxy เดียวกับที่ใช้ใน Proxifier หรือ Sandbox Instance นั้น
const proxyUrl = 'http://user:password@proxy-ip:port';
const agent = new HttpsProxyAgent(proxyUrl);

const connection = new TikTokLiveConnection('channel_username', {
    // 1. ส่ง Request ผ่าน Proxy ทั้ง HTTP และ WebSocket
    requestOptions: {
        agent: {
            https: agent,
            http: agent
        },
        timeout: 10000
    },
    websocketOptions: {
        agent: agent
    },

    // 2. ป้องกันข้อผิดพลาด "TikTok does not offer a websocket upgrade"
    // ด้วยการส่ง sessionid cookie ของบัญชีที่ล็อกอินอยู่
    sessionId: 'YOUR_TIKTOK_SESSION_ID_COOKIE',

    // 3. กำหนดพารามิเตอร์ของไคลเอนต์
    clientParams: {
        app_language: 'th-TH',
        webcast_language: 'th-TH'
    }
});
```

### คำอธิบาย Options สำคัญ:
* **`sessionId`:** ค่าคุกกี้ `sessionid` จากเบราว์เซอร์หรือจาก Profile ของ TikTok LIVE Studio ช่วยลดการถูกแคปต์ชา (Captcha) ป้องกันการโดน Rate Limit และอนุญาตให้เข้าถึงห้องไลฟ์ที่เป็นโหมดเฉพาะผู้ติดตามได้
* **`requestOptions.agent` & `websocketOptions.agent`:** บังคับให้ทราฟฟิกออกผ่าน Proxy เพื่อไม่ให้ไอพีของเซิร์ฟเวอร์หลักชนกันหลายสตรีม
* **`enableExtendedGiftInfo`:** เมื่อเปิดเป็น `true` ตัว Connector จะไปโหลดฐานข้อมูลของขวัญเพิ่มเติม ทำให้เราได้ชื่อของขวัญ (`giftName`) และจำนวนเพชร (`diamondCount`) โดยอัตโนมัติ

---

## 5. ตัวอย่างการผสานงาน: บอทตอบแชตอัตโนมัติ (Event Listener + CDP Sender)

นี่คือตัวอย่างสมบูรณ์ของการเชื่อมต่อ `TikTok-Live-Connector` เข้ากับระบบควบคุม CDP ของ TikTok LIVE Studio:

```javascript
const { TikTokLiveConnection } = require('tiktok-live-connector');
const { chromium } = require('playwright');

async function runAutoReplyBot() {
    const CDP_PORT = 9221;
    const STREAMER_USERNAME = 'my_streamer_channel';

    console.log('Connecting to TikTok LIVE Studio via CDP...');
    const browser = await chromium.connectOverCDP(`http://localhost:${CDP_PORT}`);
    
    // ค้นหา WebView ของ Chat
    let chatPage = null;
    for (const context of browser.contexts()) {
        for (const page of context.pages()) {
            if (page.url().includes('chat') || page.url().includes('webcast')) {
                chatPage = page;
                break;
            }
        }
    }

    if (!chatPage) {
        console.error('Cannot find Chat WebView inside TikTok LIVE Studio!');
        return;
    }
    console.log('Connected to Chat WebView successfully!');

    // ฟังก์ชันส่งข้อความผ่าน CDP (ปลอดภัย ไม่โดน 403)
    async function sendStudioMessage(text) {
        try {
            const textarea = chatPage.locator("textarea[placeholder*='chat']");
            await textarea.fill(text);
            await chatPage.keyboard.press('Enter');
            console.log(`[CDP Sent] -> ${text}`);
        } catch (e) {
            console.error('Failed to send message via CDP:', e.message);
        }
    }

    // เริ่มต้น TikTok-Live-Connector สำหรับดักฟังอีเวนต์
    const liveConn = new TikTokLiveConnection(STREAMER_USERNAME);
    await liveConn.connect();

    // ตัวอย่าง: เมื่อมีคนพิมพ์ !help ในแชท ให้บอทตอบผ่าน Studio อัตโนมัติ
    liveConn.on('chat', async (data) => {
        if (data.comment.trim() === '!help') {
            await sendStudioMessage(`สวัสดี @${data.uniqueId}! พิมพ์ !rules หรือ !social ได้เลยครับ`);
        }
    });

    // ตัวอย่าง: ขอบคุณเมื่อมีคนส่งของขวัญชิ้นใหญ่
    liveConn.on('gift', async (data) => {
        if (data.diamondCount >= 10 && data.repeatEnd) {
            await sendStudioMessage(`ขอขอบคุณ @${data.uniqueId} มากครับสำหรับ ${data.giftName}! 🙏🎉`);
        }
    });
}

runAutoReplyBot().catch(console.error);
```

---

## 6. รายการ Event ทั้งหมดที่รองรับ (Full Supported Events)

| Event Name | รายละเอียดที่ได้รับ | ฟิลด์ข้อมูลหลัก |
| :--- | :--- | :--- |
| `chat` | ข้อความแชตใหม่จากผู้ชม | `comment`, `uniqueId`, `userId`, `nickname`, `followRole` |
| `gift` | ของขวัญที่ถูกส่ง | `giftId`, `giftName`, `diamondCount`, `repeatCount`, `repeatEnd` |
| `like` | การเคาะหน้าจอ/กดไลก์ | `likeCount`, `totalLikeCount`, `uniqueId` |
| `member` | ผู้ชมกดเข้ามาในห้องไลฟ์ | `uniqueId`, `userId`, `displayType` |
| `roomUser` | อัปเดตสถิติยอดผู้ชมแบบเรียลไทม์ | `viewerCount` |
| `social` | การกดติดตาม (Follow) หรือแชร์ไลฟ์ (Share) | `displayType`, `label`, `uniqueId` |
| `questionNew` | ผู้ชมส่งคำถามผ่านฟังก์ชัน Q&A | `questionText`, `user` |
| `linkMicBattle` | ข้อมูลสถานะการ Battle ระหว่างช่อง | `battleUsers`, `battleStatus` |
| `linkMicArmies` | คะแนนการแข่งขัน Battle แต่ละฝั่ง | `armies`, `points` |
| `envelope` | กล่องสมบัติ (Treasure Box / Red Packet) | `coins`, `openAt`, `user` |
| `subscribe` | ผู้ชมกด Subscribe สนับสนุนช่อง | `subMonth`, `oldSubscribeStatus` |
| `streamEnd` | เจ้าของห้องปิดการไลฟ์ | `actionId` |

---

## 7. ข้อควรระวังและแนวทางแก้ไขปัญหา (Troubleshooting & Best Practices)

1. **ปัญหา "TikTok does not offer a websocket upgrade":**
   * **สาเหตุ:** IP ถูก TikTok บล็อกการเชื่อมต่อ WebSocket ชั่วคราว หรือตรวจพบว่าเป็น Anonymous Scraper
   * **ทางแก้:** 
     * กำหนด `sessionId` ใน Connection Options
     * เชื่อมต่อผ่าน Proxy ที่มีคุณภาพสูง (Residential Proxy)
     * เปิดใช้งาน polling สำรอง (`requestPollingIntervalMs: 2000`)
2. **ห้ามใช้ `sendMessage` ของ TikTok-Live-Connector โดยตรงในงาน Production:**
   * ตัวไลบรารีมีฟังก์ชัน `sendMessage` แต่ส่วนใหญ่มักล้มเหลวด้วยสถานะ **HTTP 403 Forbidden** หรือติด Captcha เนื่องจาก TikTok เข้มงวดการยิง API ภายนอกมาก
   * **ทางออกที่ดีที่สุด:** นำข้อความส่งต่อไปยัง **CDP Port** ของ TikTok LIVE Studio ตามที่ระบุในหัวข้อที่ 5 เพราะเป็นการส่งจากเซสชันของโปรแกรมทางการ 100%
3. **การอัปเดตเวอร์ชันอย่างสม่ำเสมอ:**
   * เนื่องจาก TikTok มีการเปลี่ยนแปลงโครงสร้าง Protobuf และ WebSocket Endpoints อยู่เรื่อยๆ ควรตรวจสอบการอัปเดตของไลบรารีด้วยคำสั่ง:
     ```bash
     npm update tiktok-live-connector
     ```
4. **การเลือกภาษาสำหรับการประมวลผล:**
   * หากต้องการพัฒนาด้วย **Python** สามารถใช้โปรเจกต์คู่แฝดอย่าง [isaackogan/TikTokLive](https://github.com/isaackogan/TikTokLive) ซึ่งใช้โครงสร้างและตรรกะแบบเดียวกันทุกประการ
