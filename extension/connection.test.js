import test from 'node:test';
import assert from 'node:assert/strict';
import { createConnection, reconnectAlarm } from './connection.js';

function event() {
  const listeners = [];
  return { addListener: listener => listeners.push(listener), fire: value => listeners.forEach(listener => listener(value)) };
}

async function fixture(options = {}) {
  const ports = [], timers = new Map(), messages = [], replies = [], createdAlarms = [];
  let now = 0, timerId = 0, disconnects = 0;
  const runtime = { onStartup: event(), onInstalled: event(), connectNative(name) {
    assert.equal(name, 'com.aiscreenshot.bridge');
    if (options.connectError) throw Error(options.connectError);
    const port = { onMessage: event(), onDisconnect: event(), sent: [], closed: false,
      postMessage(message) { if (this.closed) throw Error('closed port'); this.sent.push(message); },
      disconnect() { this.closed = true; this.onDisconnect.fire(); }
    };
    ports.push(port);
    return port;
  } };
  const alarms = { onAlarm: event(), get: async () => { if (options.alarmError) throw Error(options.alarmError); return options.existingAlarm; },
    create: async (...args) => { createdAlarms.push(args); }
  };
  const connection = createConnection({ runtime, alarms, browser: options.browser || 'Chrome',
    schedule: (callback, delay) => { timers.set(++timerId, { callback, due: now + delay }); return timerId; },
    cancel: id => timers.delete(id), onDisconnect: () => disconnects++,
    onMessage: (message, reply) => { messages.push(message); replies.push(reply); }
  });
  await connection.initialization;
  return { connection, runtime, alarms, ports, timers, messages, replies, createdAlarms,
    get disconnects() { return disconnects; },
    advance(ms) {
      const target = now + ms;
      while (true) {
        const next = [...timers].sort((a, b) => a[1].due - b[1].due)[0];
        if (!next || next[1].due > target) break;
        now = next[1].due; timers.delete(next[0]); next[1].callback();
      }
      now = target;
    }
  };
}

test('worker startup auto-connects and creates a durable reconnect alarm', async () => {
  const f = await fixture();
  assert.deepEqual(f.createdAlarms, [[reconnectAlarm, { periodInMinutes: 1 }]]);
  assert.deepEqual(f.ports[0].sent, [{ type: 'hello', browser: 'Chrome' }]);
  f.ports[0].onMessage.fire({ type: 'ready' });
  assert.equal(f.connection.status, '已连接（自动）');
  assert.equal(f.timers.size, 0);
});

test('Edge handshake and existing alarm survive worker recreation', async () => {
  const f = await fixture({ browser: 'Edge', existingAlarm: { name: reconnectAlarm } });
  assert.equal(f.createdAlarms.length, 0);
  assert.deepEqual(f.ports[0].sent, [{ type: 'hello', browser: 'Edge' }]);
});

test('startup, install, alarms and manual actions do not duplicate a live port', async () => {
  const f = await fixture();
  f.runtime.onStartup.fire(); f.runtime.onInstalled.fire();
  f.alarms.onAlarm.fire({ name: reconnectAlarm }); f.connection.connect();
  assert.equal(f.ports.length, 1);
});

test('app restart automatically reconnects with backoff and resets after ready', async () => {
  const f = await fixture();
  f.ports[0].onMessage.fire({ type: 'ready' }); f.ports[0].disconnect();
  assert.equal(f.disconnects, 1); assert.match(f.connection.status, /自动重连/);
  f.advance(999); assert.equal(f.ports.length, 1);
  f.advance(1); assert.equal(f.ports.length, 2);
  f.ports[1].disconnect(); f.advance(1999); assert.equal(f.ports.length, 2);
  f.advance(1); f.ports[2].onMessage.fire({ type: 'ready' }); f.ports[2].disconnect();
  f.advance(1000); assert.equal(f.ports.length, 4);
  for (const port of f.ports) assert.deepEqual(port.sent, [{ type: 'hello', browser: 'Chrome' }]);
});

test('missing Host retries safely and retains its installation error', async () => {
  const f = await fixture({ connectError: 'Specified native messaging host not found' });
  assert.match(f.connection.status, /host not found/); assert.equal(f.timers.size, 1);
  f.advance(1000); assert.match(f.connection.status, /自动重连/); assert.equal(f.timers.size, 1);
});

test('handshake timeout drops the stale port and permits a new attempt', async () => {
  const f = await fixture();
  f.advance(10000); assert.equal(f.ports[0].closed, true);
  assert.match(f.connection.status, /握手超时/);
  f.advance(1000); assert.equal(f.ports.length, 2);
});

test('manual retry and browser alarm recover promptly without waiting for JS timer', async () => {
  const f = await fixture();
  f.ports[0].disconnect(); f.connection.connect();
  assert.equal(f.ports.length, 2);
  f.ports[1].disconnect(); f.alarms.onAlarm.fire({ name: 'other' });
  assert.equal(f.ports.length, 2);
  f.alarms.onAlarm.fire({ name: reconnectAlarm }); assert.equal(f.ports.length, 3);
  f.ports[2].onMessage.fire({ type: 'ready' });
  f.advance(30000); assert.equal(f.ports.length, 3);
});

test('stale disconnects and old upload replies cannot affect the new connection', async () => {
  const f = await fixture();
  const old = f.ports[0]; old.onMessage.fire({ type: 'ready' });
  old.onMessage.fire({ type: 'commit', id: 'old-upload' });
  old.disconnect(); f.advance(1000);
  const current = f.ports[1]; current.onMessage.fire({ type: 'ready' });
  old.onDisconnect.fire(); old.onMessage.fire({ type: 'ready' });
  f.replies[0]({ type: 'result', id: 'old-upload', state: 'Unknown' });
  assert.equal(f.disconnects, 1); assert.equal(f.messages.length, 1);
  assert.deepEqual(current.sent, [{ type: 'hello', browser: 'Chrome' }]);
  assert.equal(f.connection.status, '已连接（自动）');
});

test('replies reach only the receiving connection; failed replies trigger reconnect', async () => {
  const f = await fixture();
  const port = f.ports[0]; port.onMessage.fire({ type: 'ready' });
  port.onMessage.fire({ type: 'commit', id: 'one' });
  const result = { type: 'result', id: 'one', state: 'Sent' };
  f.replies[0](result); assert.deepEqual(port.sent[1], result);
  port.closed = true;
  assert.doesNotThrow(() => f.replies[0](result));
  f.advance(1000); assert.equal(f.ports.length, 2);
});

test('app rejection clears pending chunks and is retried without replay', async () => {
  const f = await fixture();
  f.ports[0].onMessage.fire({ type: 'error', message: '同一浏览器已有连接' });
  assert.match(f.connection.status, /同一浏览器已有连接/);
  assert.equal(f.disconnects, 1); assert.equal(f.ports[0].closed, true);
  f.advance(1000); assert.equal(f.ports.length, 2); assert.equal(f.messages.length, 0);
});

test('persistent failures cap retry delay instead of launching Hosts in a tight loop', async () => {
  const f = await fixture();
  for (const delay of [1000, 2000, 5000, 10000, 30000, 30000]) {
    const count = f.ports.length;
    f.ports.at(-1).disconnect();
    f.advance(delay - 1); assert.equal(f.ports.length, count);
    f.advance(1); assert.equal(f.ports.length, count + 1);
  }
});

test('browser alarm recovers after a suspended worker has lost its JS retry timer', async () => {
  const f = await fixture();
  f.ports[0].disconnect(); f.timers.clear();
  f.alarms.onAlarm.fire({ name: reconnectAlarm });
  assert.equal(f.ports.length, 2);
  f.ports[1].onMessage.fire({ type: 'ready' });
  assert.equal(f.connection.status, '已连接（自动）');
});

test('alarm failures remain visible without preventing an ordinary connection', async () => {
  const f = await fixture({ alarmError: 'alarms unavailable' });
  assert.equal(f.ports.length, 1);
  f.ports[0].onMessage.fire({ type: 'ready' });
  assert.match(f.connection.status, /已连接/); assert.match(f.connection.status, /alarms unavailable/);
});
