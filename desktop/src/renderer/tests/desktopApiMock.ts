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

const PAIRED_COMPUTERS_DEFAULT: ApiOverrides<Api>['pairedComputers'] = {
  list: () => Promise.resolve([]),
  refresh: () => Promise.resolve(),
  onEvent: () => () => undefined
}

const SATELLITES_DEFAULT: ApiOverrides<Api>['satellites'] = {
  list: () => Promise.resolve({ supported: false, satellites: [] }),
  shareStatus: () => Promise.resolve({ installed: false, peers: [] }),
  onEvent: () => () => undefined,
  onJoinLink: () => () => undefined
}

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

const WORKSPACE_DEFAULT: ApiOverrides<Api>['workspace'] = {
  getChatsFolder: () => Promise.resolve({ path: '/workspace/chats', customized: false })
}

const APP_SERVER_DEFAULT: ApiOverrides<Api>['appServer'] = {
  hasDesktopThreadTools: () => Promise.resolve(true),
  onServerRequestSettled: () => () => undefined
}

export function installDesktopApiMock(overrides: ApiOverrides<Api>): Api {
  const api = strictApi({
    titleBarOverlayHeight: 0,
    ...overrides,
    appServer: {
      ...APP_SERVER_DEFAULT,
      sendConnectionRequest: (_key: string, method: string, params: Record<string, unknown>, timeout?: number) => {
        if (method === 'model/list' && overrides.appServer?.listModels) {
          return params.refresh === true
            ? overrides.appServer.listModels(params.providerId as string | null, true)
            : overrides.appServer.listModels(params.providerId as string | null)
        }
        const send = overrides.appServer?.sendRequest
        if (!send) throw new Error(`Unimplemented desktop API call: ${method}`)
        return timeout === undefined
          ? send(method as never, params as never)
          : send(method as never, params as never, timeout)
      },
      ...overrides.appServer
    },
    satellites: { ...SATELLITES_DEFAULT, ...overrides.satellites },
    pairedComputers: { ...PAIRED_COMPUTERS_DEFAULT, ...overrides.pairedComputers },
    desktopPet: { ...DESKTOP_PET_DEFAULT, ...overrides.desktopPet },
    screenView: { ...SCREEN_VIEW_DEFAULT, ...overrides.screenView },
    workspace: {
      ...WORKSPACE_DEFAULT,
      ...overrides.workspace,
      viewer: {
        ...overrides.workspace?.viewer,
        panelState: {
          read: () => Promise.resolve(null),
          update: () => Promise.resolve(),
          ...overrides.workspace?.viewer?.panelState
        },
        browser: {
          setForegroundScope: () => Promise.resolve(),
          restoreState: () => Promise.resolve([]),
          ...overrides.workspace?.viewer?.browser
        }
      }
    }
  }) as Api
  Object.defineProperty(window, 'api', { configurable: true, value: api })
  return api
}
