import {
  MCP_APP_SANDBOX_BRIDGE_VIOLATION_METHOD,
  MCP_APP_SANDBOX_PROXY_READY_METHOD,
  MCP_APP_SANDBOX_PROXY_URL,
  MCP_APP_SANDBOX_RESOURCE_READY_METHOD
} from '../../../../shared/mcpAppSandbox'
import {
  GENERATIVE_UI_CONNECT_METHOD,
  GENERATIVE_UI_LIMITS,
  GENERATIVE_UI_PROTOCOL_VERSION,
  GENERATIVE_UI_RUNNER_READY_METHOD,
  type GenerativeUiFailure
} from './generativeUiProtocol'
import { generativeUiRunnerDocument } from './generativeUiRunnerDocument'
import styles from './GenerativeUi.module.css'

const IDLE_TEARDOWN_MS = 60_000

interface Lease {
  connected: boolean
  onPort: (port: MessagePort) => void
  onFailure: (failure: GenerativeUiFailure) => void
}

let frame: HTMLIFrameElement | null = null
let ready = false
let frameTimer: ReturnType<typeof setTimeout> | undefined
let idleTimer: ReturnType<typeof setTimeout> | undefined
const leases = new Set<Lease>()

function ensureFrame(): void {
  clearTimeout(idleTimer)
  if (frame) return
  const created = document.createElement('iframe')
  created.className = styles.runner
  created.title = 'Generative UI runner'
  created.tabIndex = -1
  created.setAttribute('sandbox', 'allow-scripts')
  created.setAttribute('referrerpolicy', 'no-referrer')
  created.setAttribute('aria-hidden', 'true')
  frame = created
  ready = false
  window.addEventListener('message', receive)
  frameTimer = setTimeout(() => discard({
    stage: 'frame_timeout',
    message: `The sandbox did not start within ${GENERATIVE_UI_LIMITS.frameTimeoutMs / 1000} seconds.`
  }), GENERATIVE_UI_LIMITS.frameTimeoutMs)
  document.body.appendChild(created)
  created.src = MCP_APP_SANDBOX_PROXY_URL
}

function receive(event: MessageEvent): void {
  if (!frame || event.source !== frame.contentWindow) return
  const message = event.data as { method?: unknown; params?: { protocolVersion?: unknown } } | null
  if (message?.method === MCP_APP_SANDBOX_PROXY_READY_METHOD) {
    frame.contentWindow?.postMessage({ method: MCP_APP_SANDBOX_RESOURCE_READY_METHOD, params: { html: generativeUiRunnerDocument() } }, '*')
  } else if (message?.method === MCP_APP_SANDBOX_BRIDGE_VIOLATION_METHOD) {
    discard({ stage: 'host_error', message: 'The sandbox closed the runner after an oversized message.' })
  } else if (message?.method === GENERATIVE_UI_RUNNER_READY_METHOD && !ready) {
    clearTimeout(frameTimer)
    if (message.params?.protocolVersion !== GENERATIVE_UI_PROTOCOL_VERSION) {
      discard({ stage: 'protocol_mismatch', message: `Expected runner protocol version ${GENERATIVE_UI_PROTOCOL_VERSION}.` })
      return
    }
    ready = true
    for (const lease of leases) connect(lease)
  }
}

function connect(lease: Lease): void {
  if (lease.connected || !frame?.contentWindow) return
  lease.connected = true
  const channel = new MessageChannel()
  frame.contentWindow.postMessage({ method: GENERATIVE_UI_CONNECT_METHOD, params: { protocolVersion: GENERATIVE_UI_PROTOCOL_VERSION } }, '*', [channel.port2])
  lease.onPort(channel.port1)
}

function removeFrame(): void {
  clearTimeout(frameTimer)
  clearTimeout(idleTimer)
  window.removeEventListener('message', receive)
  frame?.remove()
  frame = null
  ready = false
}

function discard(failure: GenerativeUiFailure): void {
  const affected = [...leases]
  leases.clear()
  removeFrame()
  for (const lease of affected) lease.onFailure(failure)
}

function scheduleIdleTeardown(): void {
  clearTimeout(idleTimer)
  if (frame && leases.size === 0) idleTimer = setTimeout(removeFrame, IDLE_TEARDOWN_MS)
}

export function prewarmGenerativeUiRunner(): void {
  ensureFrame()
  scheduleIdleTeardown()
}

export function acquireGenerativeUiRunner(handlers: Omit<Lease, 'connected'>): () => void {
  ensureFrame()
  const lease: Lease = { connected: false, ...handlers }
  leases.add(lease)
  if (ready) connect(lease)
  return () => {
    if (leases.delete(lease)) scheduleIdleTeardown()
  }
}
