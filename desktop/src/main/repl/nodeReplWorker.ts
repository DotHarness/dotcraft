import { NativeReplEvaluator } from './NativeReplEvaluator'
import { AsyncLocalStorage } from 'node:async_hooks'
import { createConnection } from 'node:net'
import { homedir, tmpdir } from 'node:os'
import { randomUUID } from 'node:crypto'
import { isAllowedBrowserUsePipePath } from '../browserUseBackendServer'
import type { ReplContext, ReplTransport } from './protocol'

function describe(value: unknown): string {
  if (value == null) return ''
  if (typeof value === 'string') return value
  try { return JSON.stringify(value, null, 2) } catch { return String(value) }
}

export function startReplWorker(transport: ReplTransport): void {
  const scope = new AsyncLocalStorage<ReplContext>()
  let active: ReplContext | undefined
  let logs: string[] = []
  let cancelHook: ((id: string, reason: string) => Promise<void> | void) | undefined
  const pending = new Map<string, { resolve(value: unknown): void; reject(error: Error): void }>()
  const globals = globalThis as unknown as Record<string, unknown>
  const current = (): ReplContext => {
    const context = scope.getStore()
    if (!context || context !== active) throw new Error('NodeReplJs evaluation is no longer active.')
    return context
  }
  const host = (method: string, value: unknown): Promise<unknown> => {
    const context = current()
    const id = randomUUID()
    return new Promise((resolve, reject) => {
      pending.set(id, { resolve, reject })
      transport.send({ type: 'host', context, id, method, value })
    })
  }
  const write = (...values: unknown[]) => {
    if (scope.getStore() === active && active) {
      const text = values.map(describe).join(' ')
      logs.push(text)
      transport.send({ type: 'output', context: active, text })
    }
  }
  const chrome = Object.freeze({
    checkSetup: () => host('chrome.checkSetup', null),
    checkExtension: () => host('chrome.checkExtension', null),
    checkNativeHost: () => host('chrome.checkNativeHost', null)
  })
  const nativePipe = Object.freeze({
    createConnection: async (path: string) => {
      current()
      if (!isAllowedBrowserUsePipePath(path)) throw new Error('Refusing to connect to a non-DotCraft browser-use native pipe.')
      return await new Promise((resolve, reject) => {
        const socket = createConnection(path)
        socket.once('error', reject)
        socket.once('connect', () => { socket.off('error', reject); resolve(socket) })
      })
    }
  })
  const env = Object.freeze({ BROWSER_USE_AVAILABLE_BACKENDS: 'iab', BROWSER_USE_DISABLE_AMBIENT_NETWORK: '1', BROWSER_USE_SECURITY_MODE: 'disabled-for-local-testing' })
  globals.nodeRepl = Object.freeze({
    write, emitImage: (value: unknown) => host('emitImage', value),
    createElicitation: (value: unknown) => host('createElicitation', value),
    nativePipe, env, fetch, tmpDir: tmpdir(), homeDir: homedir(),
    get cwd() { return current().workspacePath },
    get requestMeta() {
      const context = current()
      const session = context.browserSession
      return { 'x-dotcraft-turn-metadata': {
        session_id: session.sessionId, thread_id: context.threadId, turn_id: session.turnId,
        evaluation_id: context.evaluationId, backend_id: session.backendId ?? 'iab'
      } }
    }
  })
  Object.defineProperty(globals, 'dotcraft', { configurable: true, get: () => Object.freeze({ ...current().dotcraft, chrome }) })
  globals.console = Object.freeze({ log: write, warn: write, error: write, info: write, debug: write })
  globals.display = (value: unknown) => host('emitImage', value)
  globals.__dotcraftSetChromeCancelHook = (hook: typeof cancelHook) => { cancelHook = hook }

  const evaluator = new NativeReplEvaluator()

  transport.listen(message => {
    if (message.type === 'hostResult') {
      if (!active || message.context.generation !== active.generation || message.context.evaluationId !== active.evaluationId) return
      const request = pending.get(message.id)
      pending.delete(message.id)
      if (message.error) request?.reject(new Error(message.error))
      else request?.resolve(message.value)
      return
    }
    if (message.type === 'cancel') {
      void (async () => {
        let error: string | undefined
        try {
          const cancel = () => cancelHook?.(message.evaluationId, 'cancelled')
          await (active ? scope.run(active, cancel) : cancel())
        } catch (cause) {
          error = String(cause)
        }
        transport.send({ type: 'cancelled', evaluationId: message.evaluationId, generation: message.generation, error })
      })()
      return
    }
    const context = message.context
    active = context
    logs = []
    scope.run(context, () => {
      void (async () => {
        try {
          process.chdir(context.workspacePath)
          const result = await evaluator.evaluate(message.code, context.workspacePath, context.evaluationId)
          if (active === context) transport.send({ type: 'result', context, result: { ...result, logs } })
        } catch (error) {
          if (active === context) transport.send({ type: 'result', context, result: { error: String(error), logs } })
        } finally {
          if (active === context) { active = undefined; pending.clear() }
        }
      })()
    })
  })
  transport.send({ type: 'ready' })
}
