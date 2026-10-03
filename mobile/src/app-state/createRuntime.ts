import { MobileSession } from '../core/session'
import { credentialStore } from '../platform/credentials'
import { deviceInfo } from '../platform/device'
import { liveNotifier } from '../platform/liveNotifier'
import { pinnedNative } from '../platform/pinnedNative'
import { stateStorage } from '../platform/storage'
import type { AppRuntime } from './runtime'

export function createRuntime(): AppRuntime {
  return {
    session: new MobileSession({
      native: pinnedNative,
      credentials: credentialStore,
      storage: stateStorage,
      device: deviceInfo(),
      live: liveNotifier,
    }),
    demo: null,
  }
}
