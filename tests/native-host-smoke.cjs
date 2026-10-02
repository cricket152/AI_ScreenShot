const { spawn } = require('node:child_process');
const path = require('node:path');
const assert = require('node:assert/strict');
const executable = process.argv[2] ? path.resolve(process.argv[2]) : path.resolve(__dirname, '../dist/AIScreenShot-win-x64/native-host/ScreenShot.NativeHost.exe');
const browser = process.argv[3] || 'Edge';
const extensionId = process.argv[4] || 'dmcdjpekhmjmcbjcaiillldpekdopocd';
assert.ok(['Chrome', 'Edge'].includes(browser), 'browser must be Chrome or Edge');
assert.match(extensionId, /^[a-p]{32}$/);
const child = spawn(executable, [`chrome-extension://${extensionId}/`], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
const timeout = setTimeout(() => { child.kill(); console.error('FAIL: native host handshake timed out; start the desktop app first'); process.exitCode = 1; }, 10000);
let received = Buffer.alloc(0);
child.stdout.on('data', data => {
  received = Buffer.concat([received, data]);
  if (received.length < 4) return;
  const length = received.readUInt32LE(0);
  assert.ok(length < 1024 * 1024);
  if (received.length < length + 4) return;
  try {
    assert.equal(JSON.parse(received.subarray(4, length + 4).toString('utf8')).type, 'ready');
    console.log('PASS: real native-host framing and current-user pipe handshake');
  } catch (error) { console.error(error.message); process.exitCode = 1; }
  clearTimeout(timeout); child.stdin.end(); child.kill();
});
child.on('error', error => { clearTimeout(timeout); console.error(error.message); process.exitCode = 1; });
child.stderr.on('data', data => process.stderr.write(data));
child.on('exit', () => { if (!received.length) { clearTimeout(timeout); console.error('FAIL: native host exited without handshake'); process.exitCode = 1; } });
const message = Buffer.from(JSON.stringify({ type: 'hello', browser }));
const header = Buffer.alloc(4); header.writeUInt32LE(message.length);
child.stdin.write(header.subarray(0, 2));
child.stdin.write(Buffer.concat([header.subarray(2), message]));
