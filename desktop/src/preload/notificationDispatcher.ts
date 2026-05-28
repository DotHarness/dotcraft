export type NotificationUnsubscribe = () => void
export type NotificationDispatchErrorHandler = (error: unknown) => void

export class TokenMulticastDispatcher<TPayload> {
  private nextToken = 0
  private readonly callbacks = new Map<number, (payload: TPayload) => void>()

  constructor(private readonly onError?: NotificationDispatchErrorHandler) {}

  subscribe(callback: (payload: TPayload) => void): NotificationUnsubscribe {
    const token = ++this.nextToken
    this.callbacks.set(token, callback)
    return () => {
      this.callbacks.delete(token)
    }
  }

  dispatch(payload: TPayload): void {
    for (const callback of [...this.callbacks.values()]) {
      try {
        callback(payload)
      } catch (error) {
        this.onError?.(error)
      }
    }
  }
}
