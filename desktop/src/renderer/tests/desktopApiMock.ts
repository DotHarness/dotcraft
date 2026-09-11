import type { Api } from '../../preload'

type ApiOverrides<T> = T extends (...args: never[]) => unknown
  ? T
  : T extends object
    ? { [K in keyof T]?: ApiOverrides<T[K]> }
    : T

function missingApi(path: string): unknown {
  return new Proxy(() => { throw new Error(`Unimplemented desktop API call: ${path}`) }, {
    get: (_, property) => missingApi(`${path}.${String(property)}`)
  })
}

function strictApi<T extends object>(overrides: T, path = 'window.api'): T {
  return new Proxy(overrides, {
    get(target, property, receiver) {
      if (!(property in target)) return missingApi(`${path}.${String(property)}`)
      const value = Reflect.get(target, property, receiver)
      return value && typeof value === 'object' ? strictApi(value, `${path}.${String(property)}`) : value
    }
  })
}

/** The app shell, Settings and the composer all mount the bridge, so it is never strict. */
const SATELLITES_DEFAULT: ApiOverrides<Api>['satellites'] = {
  list: () => Promise.resolve({ supported: false, satellites: [] }),
  shareStatus: () => Promise.resolve({ installed: false, peers: [] }),
  onEvent: () => () => undefined,
  onJoinLink: () => () => undefined
}

/** The app shell watches companion ownership from the start, so a silent bridge is the default. */
const DESKTOP_PET_DEFAULT: ApiOverrides<Api>['desktopPet'] = {
  command: () => Promise.resolve(),
  onEvent: () => () => undefined
}

const SCREEN_VIEW_DEFAULT: ApiOverrides<Api>['screenView'] = {
  open: () => Promise.resolve({ ok: true }),
  close: () => Promise.resolve({ ok: true }),
  tune: () => Promise.resolve({ ok: true }),
  ack: () => undefined,
  onFrame: () => () => undefined,
  onState: () => () => undefined
}

export function installDesktopApiMock(overrides: ApiOverrides<Api>): Api {
  const api = strictApi({
    ...overrides,
    satellites: { ...SATELLITES_DEFAULT, ...overrides.satellites },
    desktopPet: { ...DESKTOP_PET_DEFAULT, ...overrides.desktopPet },
    screenView: { ...SCREEN_VIEW_DEFAULT, ...overrides.screenView }
  }) as Api
  Object.defineProperty(window, 'api', { configurable: true, value: api })
  return api
}
