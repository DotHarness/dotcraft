import { execFileSync } from 'node:child_process'
import path from 'node:path'
import { pathToFileURL } from 'node:url'
import { expect, it } from 'vitest'

it('frames native host messages, builds extension commands with session metadata, and names its pipe', () => {
  const nativeHostPath = path.resolve(process.cwd(), 'resources/plugins/dotcraft-bundled/plugins/chrome/scripts/native-host.mjs')
  const output = execFileSync(process.execPath, ['--input-type=module', '-e', `
    const mod = await import(${JSON.stringify(pathToFileURL(nativeHostPath).href)});
    const decoder = new mod.FrameDecoder();
    const combined = Buffer.concat([
      mod.encodeFrame({ id: 1, ok: true, result: { backendId: 'chrome-extension' } }),
      mod.encodeFrame({ id: 2, ok: false, error: 'CommandTimeout: nope' })
    ]);
    const early = decoder.push(combined.subarray(0, 5));
    const decoded = decoder.push(combined.subarray(5));
    const message = mod.buildExtensionCommandMessage({
      id: 10,
      kind: 'command',
      commandId: 'cmd-1',
      method: 'tab.evaluate',
      params: { tab: { id: 7 }, timeoutMs: 999 },
      browserSession: { protocolVersion: 1, sessionId: 'thread-1', turnId: 'turn-1', evaluationId: 'eval-1' },
      timeoutMs: 1234
    }, 42);
    console.log(JSON.stringify({ early, decoded, message, pipePath: mod.nativePipePath(123, 'abc') }));
  `], { encoding: 'utf8' })
  const result = JSON.parse(output)

  expect(result.early).toEqual([])
  expect(result.decoded).toEqual([
    { id: 1, ok: true, result: { backendId: 'chrome-extension' } },
    { id: 2, ok: false, error: 'CommandTimeout: nope' }
  ])
  expect(result.message).toEqual({
    type: 'dotcraft-request',
    id: 42,
    commandId: 'cmd-1',
    method: 'tab.evaluate',
    params: {
      tab: { id: 7 },
      timeoutMs: 1234,
      browserSession: { protocolVersion: 1, sessionId: 'thread-1', turnId: 'turn-1', evaluationId: 'eval-1' },
      commandId: 'cmd-1'
    },
    timeoutMs: 1234
  })
  expect(result.pipePath).toContain('dotcraft-chrome-123-abc')
}, 20_000)
