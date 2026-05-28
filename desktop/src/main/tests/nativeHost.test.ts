import { execFileSync } from 'node:child_process'
import path from 'node:path'
import { pathToFileURL } from 'node:url'
import { describe, expect, it } from 'vitest'

const NATIVE_HOST_TEST_TIMEOUT_MS = 20_000

function runNativeHostEval(source: string) {
  const nativeHostPath = path.resolve(process.cwd(), 'resources/plugins/dotcraft-bundled/plugins/chrome/scripts/native-host.mjs')
  const moduleUrl = pathToFileURL(nativeHostPath).href
  const output = execFileSync(process.execPath, ['--input-type=module', '-e', `
    const mod = await import(${JSON.stringify(moduleUrl)});
    ${source}
  `], { encoding: 'utf8' })
  return JSON.parse(output)
}

describe('chrome native host protocol helpers', () => {
  it('encodes and decodes length-prefixed JSON frames', () => {
    const result = runNativeHostEval(`
      const decoder = new mod.FrameDecoder();
      const first = mod.encodeFrame({ id: 1, ok: true, result: { backendId: 'chrome-extension' } });
      const second = mod.encodeFrame({ id: 2, ok: false, error: 'CommandTimeout: nope' });
      const combined = Buffer.concat([first, second]);
      const early = decoder.push(combined.subarray(0, 5));
      const decoded = decoder.push(combined.subarray(5));
      console.log(JSON.stringify({ early, decoded }));
    `)

    expect(result).toEqual({
      early: [],
      decoded: [
        { id: 1, ok: true, result: { backendId: 'chrome-extension' } },
        { id: 2, ok: false, error: 'CommandTimeout: nope' }
      ]
    })
  }, NATIVE_HOST_TEST_TIMEOUT_MS)

  it('builds extension command messages with session metadata and timeout', () => {
    const result = runNativeHostEval(`
      const browserSession = {
        protocolVersion: 1,
        sessionId: 'thread-1',
        turnId: 'turn-1',
        evaluationId: 'eval-1'
      };
      const message = mod.buildExtensionCommandMessage({
        id: 10,
        kind: 'command',
        commandId: 'cmd-1',
        method: 'tab.evaluate',
        params: { tab: { id: 7 }, timeoutMs: 999 },
        browserSession,
        timeoutMs: 1234
      }, 42);
      console.log(JSON.stringify(message));
    `)

    expect(result).toEqual({
      type: 'dotcraft-request',
      id: 42,
      commandId: 'cmd-1',
      method: 'tab.evaluate',
      params: {
        tab: { id: 7 },
        timeoutMs: 1234,
        browserSession: {
          protocolVersion: 1,
          sessionId: 'thread-1',
          turnId: 'turn-1',
          evaluationId: 'eval-1'
        },
        commandId: 'cmd-1'
      },
      timeoutMs: 1234
    })
  }, NATIVE_HOST_TEST_TIMEOUT_MS)

  it('resolves platform pipe paths with the dotcraft chrome prefix', () => {
    const result = runNativeHostEval(`
      console.log(JSON.stringify({ pipePath: mod.nativePipePath(123, 'abc') }));
    `)

    expect(result.pipePath).toContain('dotcraft-chrome-123-abc')
  }, NATIVE_HOST_TEST_TIMEOUT_MS)
})
