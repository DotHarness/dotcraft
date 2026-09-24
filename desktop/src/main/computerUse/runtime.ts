import { app } from 'electron'
import { join } from 'node:path'
import { DEFAULT_LOCALE, translate, type AppLocale } from '../../shared/locales'
import type { AppSettings } from '../settings'
import { resolveWindowAppsWithPowerShell } from './appIdentity'
import type { AppRef } from './computerApi'
import { ComputerUseManager } from './ComputerUseManager'
import { CuaDriverClient, spawnCuaDriver } from './cuaDriverClient'
import lock from './cua-driver.lock.json'
import { ComputerUseStatusPill } from './statusPill'

interface ComputerUseRuntimeHost {
  getSettings(): AppSettings
  updateSettings(partial: Partial<AppSettings>): Promise<void>
}

let host: ComputerUseRuntimeHost | null = null
const pill = new ComputerUseStatusPill()

function driverPath(): string {
  const packaged = app.isPackaged && typeof process.resourcesPath === 'string'
  const root = packaged ? join(process.resourcesPath, 'bin') : join(app.getAppPath(), 'resources', 'bin')
  return join(root, 'cua-driver', 'cua-driver.exe')
}

function driverEnv(): NodeJS.ProcessEnv {
  return {
    ...process.env,
    HOME: join(app.getPath('userData'), 'computer-use'),
    CUA_DRIVER_RS_TELEMETRY_ENABLED: 'false',
    CUA_DRIVER_RS_UPDATE_CHECK: 'false',
    CUA_DRIVER_RS_SESSION_IDLE_TTL_SECS: '86400',
    CUA_LOG: 'warn'
  }
}

function locale(): AppLocale {
  return host?.getSettings().locale ?? DEFAULT_LOCALE
}

function allowedApps(): AppRef[] {
  return host?.getSettings().computerUse?.alwaysAllowedApps ?? []
}

export const computerUseManager = new ComputerUseManager({
  createDriver: () => new CuaDriverClient(spawnCuaDriver(driverPath(), driverEnv()), lock.version),
  resolveWindowApps: resolveWindowAppsWithPowerShell,
  settings: { getAlwaysAllowedApps: allowedApps },
  indicator: {
    show: (onStop) => pill.show({
      usingComputer: translate(locale(), 'computerUse.pill.usingComputer'),
      escToCancel: translate(locale(), 'computerUse.pill.escToCancel')
    }, onStop),
    hide: () => pill.hide(),
    suspendEscape: (run) => pill.suspendEscape(run)
  }
})

export async function rememberAlwaysAllowedApp(entry: AppRef): Promise<void> {
  const current = host?.getSettings().computerUse ?? {}
  const apps = current.alwaysAllowedApps ?? []
  if (!host || apps.some((app) => app.id.toLowerCase() === entry.id.toLowerCase())) return
  await host.updateSettings({ computerUse: { ...current, alwaysAllowedApps: [...apps, entry] } })
}

export function setComputerUseRuntimeHost(next: ComputerUseRuntimeHost): void {
  host = next
}

export async function disposeComputerUse(): Promise<void> {
  await computerUseManager.dispose()
  pill.dispose()
}
