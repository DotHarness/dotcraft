import type { AppServerDelivery, AppServerIpcRequest, AppServerResponseOptions, RawNotificationPayload } from '../shared/appServerBoundary'
import { unwrapAppServerResult } from '../shared/appServerError'

interface PendingResponse {
  resolve: (value: unknown) => void
  reject: (reason: unknown) => void
  onResponse?: (result: unknown) => void
}

export class AppServerDeliveryConsumer {
  private nextRequestId = 0
  private readonly pending = new Map<number, PendingResponse>()

  constructor(
    private readonly send: (request: AppServerIpcRequest) => void,
    private readonly notify: (payload: RawNotificationPayload) => void
  ) {}

  request<T>(
    method: string,
    params?: unknown,
    timeoutMs?: number | null,
    options?: AppServerResponseOptions<T>,
    connectionKey?: string
  ): Promise<T> {
    const requestId = ++this.nextRequestId
    return new Promise<T>((resolve, reject) => {
      this.pending.set(requestId, {
        resolve: value => resolve(value as T), reject,
        onResponse: options?.onResponse as ((result: unknown) => void) | undefined
      })
      try {
        this.send({ requestId, method, params, timeoutMs, connectionKey, connectionId: options?.connectionId, attachmentOwner: options?.attachmentOwner })
      } catch (error) {
        this.pending.delete(requestId)
        reject(error)
      }
    })
  }

  accept(delivery: AppServerDelivery): void {
    if (delivery.kind === 'notification') {
      this.notify(delivery.payload)
      return
    }
    const pending = this.pending.get(delivery.requestId)
    if (!pending) return
    this.pending.delete(delivery.requestId)
    try {
      const result = unwrapAppServerResult(delivery.result)
      pending.onResponse?.(result)
      pending.resolve(result)
    } catch (error) {
      pending.reject(error)
    }
  }
}
