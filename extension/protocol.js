export class Assembly {
  constructor() { this.pending = new Map(); }
  accept(message) {
    if (message.type === 'begin') {
      if (!Number.isSafeInteger(message.length) || message.length <= 0 || message.length > 64 * 1024 * 1024 || typeof message.prompt !== 'string' || !/^[a-f0-9]{64}$/.test(message.sha256)) throw Error('Invalid image metadata');
      this.pending.clear();
      this.pending.set(message.id, { ...message, parts: [], encodedParts: [], received: 0 });
      return;
    }
    const item = this.pending.get(message.id);
    if (!item) throw Error('Missing image metadata');
    if (message.type === 'chunk') {
      if (message.index !== item.parts.length || typeof message.data !== 'string' || message.data.length > 256 * 1024) throw Error('Invalid chunk');
      const bytes = Uint8Array.from(atob(message.data), c => c.charCodeAt(0));
      item.received += bytes.length;
      if (item.received > item.length) throw Error('Image exceeds declared length');
      item.parts.push(bytes);
      item.encodedParts.push(message.data);
      return;
    }
    if (message.type !== 'commit') throw Error('Unknown message');
    this.pending.delete(message.id);
    if (item.received !== item.length) throw Error('Incomplete image');
    const bytes = new Uint8Array(item.length);
    let offset = 0;
    for (const part of item.parts) { bytes.set(part, offset); offset += part.length; }
    return { ...item, bytes };
  }
}
