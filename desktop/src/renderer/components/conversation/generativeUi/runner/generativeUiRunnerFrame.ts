import {
  GENERATIVE_UI_CONNECT_METHOD,
  GENERATIVE_UI_LIMITS,
  GENERATIVE_UI_PROTOCOL_VERSION,
  GENERATIVE_UI_RUNNER_READY_METHOD,
  GENERATIVE_UI_WORKER_SOURCE_ELEMENT_ID,
  type GenerativeUiFailureStage
} from '../generativeUiProtocol'

let workerUrl: string | null = null

const workerSourceUrl = (): string => {
  if (!workerUrl) {
    const source = JSON.parse(document.getElementById(GENERATIVE_UI_WORKER_SOURCE_ELEMENT_ID)?.textContent ?? '""') as string
    workerUrl = URL.createObjectURL(new Blob([source], { type: 'text/javascript' }))
  }
  return workerUrl
}

const challenge = (): string => Array.from(crypto.getRandomValues(new Uint32Array(4)), part => part.toString(16)).join('')

const connect = (channel: MessagePort, protocolVersion: unknown): void => {
  let worker: Worker | null = null
  let started = false
  let lastPong = 0
  const challenges = new Set<string>()
  let watchdog: ReturnType<typeof setInterval> | undefined

  const terminate = (): void => {
    clearInterval(watchdog)
    worker?.terminate()
    worker = null
  }
  const stop = (stage: GenerativeUiFailureStage, message: string): void => {
    terminate()
    channel.postMessage({ protocolVersion: GENERATIVE_UI_PROTOCOL_VERSION, type: 'error', stage, message })
    channel.close()
  }

  if (protocolVersion !== GENERATIVE_UI_PROTOCOL_VERSION) {
    stop('protocol_mismatch', `The runner speaks protocol version ${GENERATIVE_UI_PROTOCOL_VERSION}.`)
    return
  }
  try {
    worker = new Worker(workerSourceUrl())
  } catch (error) {
    stop('worker_start_error', error instanceof Error ? error.message : String(error))
    return
  }
  lastPong = Date.now()
  worker.addEventListener('message', event => {
    const data = event.data as { type?: unknown; challenge?: unknown } | null
    if (data?.type === 'pong') {
      if (typeof data.challenge !== 'string' || !challenges.has(data.challenge)) return
      for (const sent of challenges) {
        challenges.delete(sent)
        if (sent === data.challenge) break
      }
      lastPong = Date.now()
      return
    }
    started = true
    channel.postMessage(event.data)
  })
  worker.addEventListener('error', event => {
    event.preventDefault()
    stop(started ? 'runtime_error' : 'worker_start_error', event.message || 'The worker stopped unexpectedly.')
  })
  worker.addEventListener('messageerror', () => stop('invalid_payload', 'The worker sent a message that could not be read.'))
  channel.addEventListener('message', event => {
    worker?.postMessage(event.data)
    if ((event.data as { type?: unknown } | null)?.type !== 'dispose') return
    terminate()
    channel.close()
  })
  channel.start()
  watchdog = setInterval(() => {
    if (Date.now() - lastPong > GENERATIVE_UI_LIMITS.workerUnresponsiveMs) {
      stop('worker_timeout', `The program stopped responding for more than ${GENERATIVE_UI_LIMITS.workerUnresponsiveMs / 1000} seconds.`)
      return
    }
    const next = challenge()
    challenges.add(next)
    worker?.postMessage({ type: 'ping', challenge: next })
  }, GENERATIVE_UI_LIMITS.workerPingIntervalMs)
}

window.addEventListener('message', event => {
  if (event.source !== window.parent) return
  const message = event.data as { method?: unknown; params?: { protocolVersion?: unknown } } | null
  const channel = event.ports[0]
  if (message?.method !== GENERATIVE_UI_CONNECT_METHOD || !channel) return
  connect(channel, message.params?.protocolVersion)
})

window.parent.postMessage({ method: GENERATIVE_UI_RUNNER_READY_METHOD, params: { protocolVersion: GENERATIVE_UI_PROTOCOL_VERSION } }, '*')
