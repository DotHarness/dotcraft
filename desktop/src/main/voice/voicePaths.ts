import { homedir } from 'os'
import { join } from 'path'

/** Resolves the user-scoped cache root for managed Voice Input assets. */
export function resolveVoiceRoot(home = homedir()): string {
  return join(home, '.craft', 'cache', 'voice')
}
