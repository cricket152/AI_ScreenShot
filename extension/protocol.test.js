import test from 'node:test';
import assert from 'node:assert/strict';
import { Assembly } from './protocol.js';
const metadata = length => ({ type: 'begin', id: '1', length, prompt: '中文提示词', sha256: 'a'.repeat(64) });
test('large images survive ordered chunk reassembly', () => {
  const original = Uint8Array.from({ length: 900000 }, (_, i) => i % 251);
  const assembly = new Assembly(); assembly.accept(metadata(original.length));
  for (let offset = 0, index = 0; offset < original.length; offset += 192 * 1024, index++) {
    assembly.accept({ type: 'chunk', id: '1', index, data: Buffer.from(original.slice(offset, offset + 192 * 1024)).toString('base64') });
  }
  const result = assembly.accept({ type: 'commit', id: '1' });
  assert.deepEqual(result.bytes, original);
  assert.deepEqual(Buffer.concat(result.encodedParts.map(part => Buffer.from(part, 'base64'))), Buffer.from(original));
});
test('rejects out of order, incomplete and oversized images', () => {
  const assembly = new Assembly();
  assert.throws(() => assembly.accept(metadata(65 * 1024 * 1024)));
  assembly.accept(metadata(4));
  assert.throws(() => assembly.accept({ type: 'chunk', id: '1', index: 1, data: 'AA==' }));
  assert.throws(() => assembly.accept({ type: 'commit', id: '1' }));
});
