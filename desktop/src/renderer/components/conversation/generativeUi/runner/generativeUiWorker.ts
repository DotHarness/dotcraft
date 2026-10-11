import { createGenerativeUiRuntime } from './generativeUiRuntime'

const BLOCKED_GLOBALS = [
  'fetch',
  'XMLHttpRequest',
  'WebSocket',
  'WebTransport',
  'EventSource',
  'importScripts',
  'Worker',
  'SharedWorker',
  'BroadcastChannel',
  'indexedDB',
  'caches'
]

interface WorkerScope {
  postMessage: (message: unknown) => void
  addEventListener(type: 'message', listener: (event: MessageEvent) => void): void
  addEventListener(type: 'error', listener: (event: ErrorEvent) => void): void
  addEventListener(type: 'unhandledrejection', listener: (event: PromiseRejectionEvent) => void): void
}

const scope = self as unknown as WorkerScope
const post = scope.postMessage.bind(scope)

for (let target: object | null = scope; target; target = Object.getPrototypeOf(target)) {
  for (const name of BLOCKED_GLOBALS) {
    if (!Object.prototype.hasOwnProperty.call(target, name)) continue
    try {
      Object.defineProperty(target, name, { value: undefined, writable: false, configurable: false })
    } catch {
      Reflect.deleteProperty(target, name)
    }
  }
}

const runtime = createGenerativeUiRuntime(post)

scope.addEventListener('message', event => {
  if ((event.data as { type?: unknown } | null)?.type === 'ping') {
    post({ type: 'pong' })
    return
  }
  runtime.receive(event.data)
})
scope.addEventListener('error', event => {
  event.preventDefault()
  runtime.reportUncaught(event.error ?? new Error(event.message))
})
scope.addEventListener('unhandledrejection', event => {
  event.preventDefault()
  runtime.reportUncaught(event.reason)
})
