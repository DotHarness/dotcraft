import { File, Paths } from 'expo-file-system'
import type { StateStorage } from '../core/session'
import type { PersistedState } from '../core/state'

const file = () => new File(Paths.document, 'dotcraft-mobile.json')

export const stateStorage: StateStorage = {
  async load() {
    const target = file()
    if (!target.exists) return null
    try {
      return JSON.parse(await target.text()) as PersistedState
    } catch {
      return null
    }
  },
  async save(state) {
    file().write(JSON.stringify(state))
  },
  async clear() {
    const target = file()
    if (target.exists) target.delete()
  },
}
