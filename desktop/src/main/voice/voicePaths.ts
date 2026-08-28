import { homedir } from 'os'
import { join } from 'path'

export function resolveVoiceRoot(home = homedir()): string {
  return join(home, '.craft', 'cache', 'voice')
}
