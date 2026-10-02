import { Assembly } from './protocol.js';
import { createConnection } from './connection.js';
const assembly = new Assembly();
let busy = false;
const connection = createConnection({
  runtime: chrome.runtime,
  alarms: chrome.alarms,
  browser: /Edg\//.test(navigator.userAgent) ? 'Edge' : 'Chrome',
  onDisconnect: () => assembly.pending.clear(),
  onMessage: async (message, reply) => {
    let dispatched = false;
    let ownsBusy = false;
    try {
      const request = assembly.accept(message);
      if (!request) return;
      if (busy) throw Error('已有发送正在进行');
      busy = true;
      ownsBusy = true;
      const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', request.bytes));
      if (Array.from(digest, x => x.toString(16).padStart(2, '0')).join('') !== request.sha256) throw Error('截图校验失败');
      const tab = await chrome.tabs.create({ url: 'https://chatgpt.com/', active: true });
      await waitForTab(tab.id);
      dispatched = true;
      const result = await chrome.scripting.executeScript({ target: { tabId: tab.id }, world: 'MAIN', func: sendToChatGPT, args: [request.encodedParts, request.prompt] });
      const value = result[0]?.result || { state: 'Unknown', message: '页面没有返回发送结果，请检查新标签页' };
      reply({ type: 'result', id: request.id, ...value });
    } catch (error) {
      reply({ type: 'result', id: message.id, state: dispatched ? 'Unknown' : 'Failed', message: dispatched ? '页面执行结果未知，请检查新标签页；不会自动重发' : String(error.message || error) });
    } finally { if (ownsBusy) busy = false; }
  }
});
async function waitForTab(id) {
  const deadline = Date.now() + 30000;
  while (Date.now() < deadline) {
    if ((await chrome.tabs.get(id)).status === 'complete') return;
    await new Promise(resolve => setTimeout(resolve, 150));
  }
  throw Error('ChatGPT 页面加载超时');
}
// Runs in the target tab; must remain self-contained.
async function sendToChatGPT(encodedParts, prompt) {
  let clicked = false;
  const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
  async function wait(predicate, timeout = 15000) {
    const deadline = Date.now() + timeout;
    while (Date.now() < deadline) { const value = predicate(); if (value) return value; await pause(150); }
    throw Error('页面控件或附件未就绪，请检查登录、上传错误或页面结构变化');
  }
  const visible = element => element && element.getClientRects().length > 0;
  const composer = () => Array.from(document.querySelectorAll('#prompt-textarea,textarea,[contenteditable="true"][role="textbox"]')).find(visible);
  const userMessages = () => Array.from(document.querySelectorAll('[data-message-author-role="user"]'));
  try {
    if (location.origin !== 'https://chatgpt.com') throw Error('目标页面不是 ChatGPT');
    const editor = await wait(composer);
    const transfer = new DataTransfer();
    const imageParts = encodedParts.map(part => Uint8Array.from(atob(part), char => char.charCodeAt(0)));
    transfer.items.add(new File(imageParts, 'screenshot.png', { type: 'image/png' }));
    editor.focus();
    editor.dispatchEvent(new ClipboardEvent('paste', { bubbles: true, cancelable: true, clipboardData: transfer }));
    const scope = editor.closest('form') || editor.parentElement?.parentElement;
    if (!scope) throw Error('无法确认输入区域');
    // Never equate an enabled send button alone with a successful upload.
    await wait(() => scope.querySelector('img[src^="blob:"],img[alt*="screenshot"],button[aria-label*="Remove"],button[aria-label*="移除"],button[aria-label*="删除附件"],[data-testid*="attachment"]'), 60000);
    const current = await wait(composer);
    current.focus();
    if (current.tagName === 'TEXTAREA') {
      Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype, 'value').set.call(current, prompt);
      current.dispatchEvent(new Event('input', { bubbles: true }));
    } else {
      const selection = window.getSelection(); const range = document.createRange();
      range.selectNodeContents(current); selection.removeAllRanges(); selection.addRange(range);
      if (!document.execCommand('insertText', false, prompt)) throw Error('无法填入提示词');
    }
    const button = await wait(() => {
      const element = document.querySelector('button[data-testid*="send-button"],button[aria-label="Send prompt"],button[aria-label="发送提示"]');
      const loading = scope.querySelector('[role="progressbar"],button[aria-label*="Cancel upload"],button[aria-label*="取消上传"]');
      return visible(element) && !element.disabled && !loading && current.textContent?.trim() === prompt.trim() || (visible(element) && !element.disabled && !loading && current.value === prompt) ? element : null;
    }, 60000);
    const previous = userMessages().length;
    clicked = true; button.click();
    await wait(() => userMessages().slice(previous).some(message => message.textContent.includes(prompt) && message.querySelector('img')), 20000);
    return { state: 'Sent', message: '已发送到 ChatGPT 新对话' };
  } catch (error) {
    return { state: clicked ? 'Unknown' : 'Failed', message: clicked ? '发送结果未知，请检查 ChatGPT 新标签页；不会自动重发' : String(error.message || error) };
  }
}
chrome.runtime.onMessage.addListener((message, sender, reply) => {
  if (message.type === 'connect') connection.connect();
  reply({ status: connection.status });
});
