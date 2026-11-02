import { describe, expect, it, vi } from 'vitest'
import { AppUpdateRuntime } from '../appUpdateRuntime'

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => { resolve = done })
  return { promise, resolve }
}

describe('AppUpdateRuntime', () => {
  it('prevents queued and future operations after preparation starts', async () => {
    const runtime = new AppUpdateRuntime()
    const operation = vi.fn(async () => 1)
    const pending = runtime.run(operation)
    runtime.quiesce()
    await expect(pending).rejects.toThrow('preparing')
    await expect(runtime.run(operation)).rejects.toThrow('preparing')
    expect(operation).not.toHaveBeenCalled()
    expect(runtime.isPreparing).toBe(true)
  })

  it('waits for already-running ensures and rejects their stale results', async () => {
    const runtime = new AppUpdateRuntime()
    const request = deferred<string>()
    const result = runtime.run(() => request.promise)
    await Promise.resolve()
    runtime.quiesce()
    const idle = vi.fn()
    const wait = runtime.waitForIdle(new AbortController().signal).then(idle)
    await Promise.resolve()
    expect(idle).not.toHaveBeenCalled()
    request.resolve('late endpoint')
    await expect(result).rejects.toThrow('preparing')
    await wait
    expect(idle).toHaveBeenCalledOnce()
  })

  it('bounds waiting for in-flight work without allowing it to restart the runtime', async () => {
    const runtime = new AppUpdateRuntime()
    const request = deferred<void>()
    const result = runtime.run(() => request.promise)
    await Promise.resolve()
    runtime.quiesce()
    const controller = new AbortController()
    const waiting = runtime.waitForIdle(controller.signal)
    controller.abort(new Error('Preparation timed out'))
    await expect(waiting).rejects.toThrow('Preparation timed out')
    expect(runtime.isPreparing).toBe(true)
    request.resolve()
    await expect(result).rejects.toThrow('preparing')
    await runtime.waitForIdle(new AbortController().signal)
  })

})
