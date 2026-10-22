const { io } = require('socket.io-client');

const SERVER_URL = 'https://api.flowultratech-captcha.shop';

async function run() {
  console.log('[CLIENT SIM] Logging in...');
  const loginRes = await fetch(`${SERVER_URL}/api/v1/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      username: 'root',
      password: '!WFXU#v1aO7338+',
      hwid: 'LIVE-CLIENT-PC-TH99'
    })
  }).then(r => r.json());

  if (!loginRes.accessToken) {
    console.error('[CLIENT SIM] Login failed:', loginRes);
    process.exit(1);
  }

  const token = loginRes.accessToken;
  console.log('[CLIENT SIM] Login successful. Acquiring Slot Lease for Acc01...');

  // Acquire Slot Lease
  const leaseRes = await fetch(`${SERVER_URL}/api/v1/slots/acquire`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${token}`
    },
    body: JSON.stringify({
      profileFolderName: 'Acc01_OfficialStore',
      hwid: 'LIVE-CLIENT-PC-TH99'
    })
  }).then(r => r.json());

  console.log('[CLIENT SIM] Slot Lease acquired:', leaseRes);
  const activeLeaseId = leaseRes.leaseId || 'lease-live-default';

  // Connect via Socket.io with clientType: desktop
  console.log('[CLIENT SIM] Connecting via Socket.io as desktop client...');
  const socket = io(SERVER_URL, {
    auth: { token, clientType: 'desktop' },
    query: { clientType: 'desktop' },
    transports: ['websocket']
  });

  socket.on('connect', () => {
    console.log('[CLIENT SIM] Socket connected with id:', socket.id);

    const payload = {
      appVersion: '1.1.0',
      hwid: 'LIVE-CLIENT-PC-TH99',
      hostname: 'TH-STREAMING-RIG-01',
      isProgramOpen: true,
      runningCount: 1,
      totalCount: 2,
      instances: [
        {
          name: 'TikTok LIVE Official Store',
          accountName: 'TikTok LIVE Official Store',
          profileFolderName: 'Acc01_OfficialStore',
          cdpPort: 9222,
          status: 'Running',
          isTikTokRunning: true,
          processId: 10452,
          startedAt: new Date(Date.now() - 360000).toISOString(),
          uptimeSeconds: 360,
          proxy: 'Direct',
          isLoggedIn: true,
          channelName: 'FlowUltra Official Store',
          channelHandle: '@flowultra_th',
          channelAvatar: 'https://images.unsplash.com/photo-1618005182384-a83a8bd57fbe?w=100&auto=format&fit=crop',
          activeLeaseId: activeLeaseId,
          captcha: {
            hasAlert: false,
            isSolving: false,
            typeDescription: '-',
            countdown: '00:00',
            count: 0
          }
        },
        {
          name: 'TikTok Backup Channel',
          accountName: 'TikTok Backup Channel',
          profileFolderName: 'Acc02_Backup',
          cdpPort: 9223,
          status: 'Stopped',
          isTikTokRunning: false,
          proxy: 'Direct',
          isLoggedIn: false,
          channelName: '',
          channelHandle: '',
          activeLeaseId: '',
          captcha: {
            hasAlert: false,
            isSolving: false,
            typeDescription: '-',
            countdown: '00:00',
            count: 0
          }
        }
      ]
    };

    console.log('[CLIENT SIM] Emitting client:instances_report...');
    socket.emit('client:instances_report', payload);

    // Keep lease alive every 10s
    setInterval(() => {
      socket.emit('slot:heartbeat', { leaseId: activeLeaseId, hwid: 'LIVE-CLIENT-PC-TH99' }, (ack) => {
        console.log('[CLIENT SIM] Heartbeat ack:', ack?.success, 'expiresAt:', ack?.expiresAt);
      });
    }, 10000);
  });

  socket.on('disconnect', (reason) => {
    console.log('[CLIENT SIM] Disconnected:', reason);
  });

  // Keep script running for 180s
  setTimeout(async () => {
    console.log('[CLIENT SIM] Cleaning up lease...');
    await fetch(`${SERVER_URL}/api/v1/slots/release`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${token}`
      },
      body: JSON.stringify({ leaseId: activeLeaseId })
    });
    socket.disconnect();
    process.exit(0);
  }, 180000);
}

run().catch(err => {
  console.error('[CLIENT SIM] Fatal error:', err);
  process.exit(1);
});
