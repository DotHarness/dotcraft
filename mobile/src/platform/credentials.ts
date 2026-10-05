import * as SecureStore from 'expo-secure-store'
import type { CredentialStore } from '../core/session'

const key = (computerId: string) => `dotcraft.deviceCredential.${computerId}`

export const credentialStore: CredentialStore = {
  get: (computerId) => SecureStore.getItemAsync(key(computerId)).catch(() => null),
  set: (computerId, credential) => SecureStore.setItemAsync(key(computerId), credential),
  clear: (computerId) => SecureStore.deleteItemAsync(key(computerId)),
}
