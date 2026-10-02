const label = document.getElementById('status');
async function refresh(type = 'status') { label.textContent = (await chrome.runtime.sendMessage({ type })).status; }
document.getElementById('connect').addEventListener('click', () => refresh('connect'));
refresh();
setInterval(() => refresh().catch(() => { label.textContent = '扩展连接已断开，请重新打开弹窗'; }), 1000);
