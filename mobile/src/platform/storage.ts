import { deleteAsync, documentDirectory, moveAsync, readAsStringAsync, writeAsStringAsync } from 'expo-file-system/legacy'
import type { StateStorage } from '../core/session'
import type { PersistedState } from '../core/state'

const path = `${documentDirectory}dotcraft-mobile.json`
const temporary = `${path}.tmp`

export const stateStorage: StateStorage = {
  async load() {
    try {
      return JSON.parse(await readAsStringAsync(path)) as PersistedState
    } catch {
      return null
    }
  },
  async save(state) {
    await writeAsStringAsync(temporary, JSON.stringify(state))
    await moveAsync({ from: temporary, to: path })
  },
  async clear() {
    await deleteAsync(temporary, { idempotent: true })
    await deleteAsync(path, { idempotent: true })
  },
}
