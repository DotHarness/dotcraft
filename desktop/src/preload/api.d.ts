import type { Api } from './index'

// Keep renderer imports type-only while making the preload implementation the
// single source of truth for both the exposed API and its bridge DTOs.
export type * from './index'
export type { ThemeMode } from '../shared/theme'
export type {
  BrowserEventPayload,
  TerminalDataEventPayload,
  TerminalExitEventPayload
} from '../shared/viewer/types'

declare global {
  interface Window {
    api: Api
  }
}
