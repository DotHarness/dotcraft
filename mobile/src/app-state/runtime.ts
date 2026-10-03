import type { MobileSession } from '../core/session'

export interface DemoHooks {
  scanPayload(): string
}

export interface AppRuntime {
  session: MobileSession
  demo: DemoHooks | null
  locale?: string
}
