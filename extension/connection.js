export const reconnectAlarm = 'aiscreenshot-reconnect';
const retryDelays = [1000, 2000, 5000, 10000, 30000];

export function createConnection({ runtime, alarms, browser, onMessage, onDisconnect, schedule = setTimeout, cancel = clearTimeout }) {
  let port = null;
  let ready = false;
  let retryTimer = null;
  let handshakeTimer = null;
  let failures = 0;
  let status = '等待截图工具，正在自动连接…';
  let alarmError = '';

  function clearTimer() {
    if (retryTimer !== null) cancel(retryTimer);
    retryTimer = null;
  }

  function retry(reason) {
    status = `${reason}；将自动重连，请确认截图工具已启动。`;
    if (retryTimer !== null) return;
    const delay = retryDelays[Math.min(failures++, retryDelays.length - 1)];
    retryTimer = schedule(() => { retryTimer = null; connect(); }, delay);
  }

  function disconnected(current, reason) {
    if (port !== current) return;
    port = null;
    ready = false;
    if (handshakeTimer !== null) cancel(handshakeTimer);
    handshakeTimer = null;
    onDisconnect();
    retry(reason);
  }

  function connect() {
    if (port) return;
    clearTimer();
    status = '正在自动连接截图工具…';
    let current;
    try {
      current = runtime.connectNative('com.aiscreenshot.bridge');
      port = current;
      current.onDisconnect.addListener(() => {
        const reason = runtime.lastError?.message || '连接已断开';
        disconnected(current, reason);
      });
      current.onMessage.addListener(message => {
        if (port !== current) return;
        if (message.type === 'ready') {
          ready = true;
          failures = 0;
          status = '已连接（自动）';
          if (handshakeTimer !== null) cancel(handshakeTimer);
          handshakeTimer = null;
          return;
        }
        if (message.type === 'error') {
          disconnected(current, message.message || '连接被截图工具拒绝');
          current.disconnect();
          return;
        }
        // Bind replies to the receiving port. An old upload must never report
        // its result to a new connection after the desktop program restarts.
        if (ready) onMessage(message, reply => {
          if (port !== current || !ready) return;
          try { current.postMessage(reply); }
          catch (error) {
            disconnected(current, String(error.message || error));
            try { current.disconnect(); } catch { /* Already disconnected. */ }
          }
        });
      });
      handshakeTimer = schedule(() => {
        if (port !== current || ready) return;
        disconnected(current, '等待截图工具握手超时');
        current.disconnect();
      }, 10000);
      current.postMessage({ type: 'hello', browser });
    } catch (error) {
      if (current && port === current) {
        disconnected(current, String(error.message || error));
        try { current.disconnect(); } catch { /* Already disconnected. */ }
      } else {
        retry(String(error.message || error));
      }
    }
  }

  // MV3 can discard JS timers. A browser alarm also wakes a suspended worker.
  // One minute works across Chromium versions without raising the minimum version.
  alarms.onAlarm.addListener(alarm => { if (alarm.name === reconnectAlarm) connect(); });
  runtime.onStartup.addListener(connect);
  runtime.onInstalled.addListener(connect);
  const initialization = (async () => {
    try {
      if (!await alarms.get(reconnectAlarm)) await alarms.create(reconnectAlarm, { periodInMinutes: 1 });
    } catch (error) {
      // Preserve the error in the UI; ordinary in-session retries still work.
      alarmError = `后台自动重连定时器不可用：${String(error.message || error)}`;
    }
    connect();
  })();
  return { connect, get status() { return alarmError ? `${status} · ${alarmError}` : status; }, initialization };
}
