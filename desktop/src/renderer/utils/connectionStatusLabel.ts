import type { ConnectionStatus } from '../stores/connectionStore'
import type { MessageKey } from '../../shared/locales'

type T = (key: MessageKey | string, vars?: Record<string, string | number>) => string

/** User-visible label for the current AppServer connection state (sidebar, tooltips). */
export function connectionStatusLabel(
  status: ConnectionStatus,
  errorMessage: string | null,
  t: T
): string {
  switch (status) {
    case 'connecting':
      return t('connection.connecting')
    case 'connected':
      return t('connection.connected')
    case 'disconnected':
      return t('connection.disconnected')
    case 'error':
      return errorMessage?.trim() || t('connection.unknownError')
    default:
      return t('connection.unknownError')
  }
}
