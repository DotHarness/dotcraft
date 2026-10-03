import * as SecureStore from 'expo-secure-store'
import type { CredentialStore } from '../core/session'

const KEY = 'dotcraft.deviceCredential'

export const credentialStore: CredentialStore = {
  get: () => SecureStore.getItemAsync(KEY).catch(() => null),
  set: (credential) => SecureStore.setItemAsync(KEY, credential),
  clear: () => SecureStore.deleteItemAsync(KEY),
}
