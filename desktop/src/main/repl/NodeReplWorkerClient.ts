import { utilityProcess } from 'electron'
import { randomUUID } from 'node:crypto'
import { join } from 'node:path'
import type { ReplContext, ReplRequest, ReplResponse, ReplResult } from './protocol'

export interface ReplProcess {
  postMessage(message: ReplRequest): void
  on(event: 'message', listener: (message: ReplResponse) => void): unknown
  on(event: 'exit', listener: (code: number) => void): unknown
  kill(): unknown
}
export type ForkReplWorker = () => ReplProcess
export const defaultForkReplWorker: ForkReplWorker = () => utilityProcess.fork(
  join(__dirname, 'nodeReplWorker.js'), [], { serviceName: 'DotCraft Node REPL', stdio: 'ignore' }
)

type HostCall = (method: string, value: unknown) => Promise<unknown>

export class NodeReplWorkerClient {
  private readonly generation = randomUUID()
  private readonly child: ReplProcess
  private active?: { context: ReplContext; host: HostCall; resolve(result: ReplResult): void; reject(error: Error): void }
  private cancelAck?: () => void
  private stopping?: Promise<void>
  readonly logs: string[] = []
  private ready!: Promise<void>
  private started!: () => void
  private exited = false
  closed = false

  constructor(fork: ForkReplWorker = defaultForkReplWorker) {
    this.ready = new Promise(resolve => { this.started = resolve })
    this.child = fork()
    this.child.on('message', message => { void this.receive(message) })
    this.child.on('exit', code => {
      this.exited = true
      this.closed = true
      this.started()
      this.active?.reject(new Error(`Node REPL process exited (${code}).`))
      this.active = undefined
      this.cancelAck?.()
    })
  }

  async evaluate(context: Omit<ReplContext, 'generation'>, code: string, host: HostCall): Promise<ReplResult> {
    await this.ready
    if (this.closed) throw new Error('Node REPL process is closed.')
    this.logs.length = 0
    return new Promise((resolve, reject) => {
      const scoped = { ...context, generation: this.generation }
      this.active = { context: scoped, host, resolve, reject }
      this.child.postMessage({ type: 'evaluate', context: scoped, code })
    })
  }

  stop(evaluationId?: string): Promise<void> {
    if (this.stopping) return this.stopping
    if (this.exited) return Promise.resolve()
    this.closed = true
    this.started()
    this.stopping = (async () => {
      if (evaluationId) {
        await new Promise<void>(resolve => {
          // A busy evaluator cannot acknowledge cancellation; the parent must still be able to kill it.
          const timer = setTimeout(resolve, 100)
          this.cancelAck = () => { clearTimeout(timer); resolve() }
          this.child.postMessage({ type: 'cancel', evaluationId, generation: this.generation })
        })
      }
      this.child.kill()
      this.active?.reject(new Error('Node REPL process stopped.'))
      this.active = undefined
    })()
    return this.stopping
  }

  private async receive(message: ReplResponse): Promise<void> {
    if (message.type === 'ready') { this.started(); return }
    if (message.type === 'cancelled') {
      if (message.generation === this.generation) {
        if (message.error) this.logs.push(`Chrome cancellation failed: ${message.error}`)
        this.cancelAck?.()
      }
      return
    }
    const active = this.active
    if (this.closed || !active || message.context.generation !== this.generation ||
      message.context.threadId !== active.context.threadId || message.context.evaluationId !== active.context.evaluationId) return
    if (message.type === 'output') { this.logs.push(message.text); return }
    if (message.type === 'result') {
      this.active = undefined
      active.resolve(message.result)
      return
    }
    try {
      const value = await active.host(message.method, message.value)
      if (!this.closed && this.active === active) this.child.postMessage({ type: 'hostResult', context: active.context, id: message.id, value })
    } catch (error) {
      if (!this.closed && this.active === active) this.child.postMessage({ type: 'hostResult', context: active.context, id: message.id, error: String(error) })
    }
  }
}
