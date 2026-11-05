import { app, Notification } from 'electron'
import { join } from 'node:path'
import { existsSync } from 'node:fs'
import { DEFAULT_LOCALE, translate, type AppLocale } from '../shared/locales'
import { resolveTaskCompletionNotificationMode, type AppSettings } from './settings'
import type { HubEvent } from './desktopHub'

export interface HubNotificationPayload {
  kind: string | null
  workspacePath?: string | null
  threadId: string | null
  title: string
  body: string | null
  actionUrl: string | null
  openDesktopOnClick: boolean | null
}

function vars(value: unknown): Record<string, string | number> | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined
  return Object.fromEntries(
    Object.entries(value).flatMap(([key, item]) =>
      typeof item === 'string' || typeof item === 'number'
        ? [[key, item]]
        : typeof item === 'boolean'
          ? [[key, String(item)]]
          : []
    )
  )
}

function text(
  locale: AppLocale,
  key: unknown,
  params: unknown,
  fallback: unknown,
  legacy: unknown
): string | null {
  const value = typeof fallback === 'string' ? fallback : typeof legacy === 'string' ? legacy : null
  if (typeof key !== 'string' || !key.trim()) return value
  const localized = translate(locale, key.trim(), vars(params))
  return localized === key.trim() ? value : localized
}

export function resolveNotificationIconPath(platform: NodeJS.Platform = process.platform): string | null {
  const root = app.isPackaged ? process.resourcesPath : join(__dirname, '../../resources')
  return (
    (platform === 'win32' ? ['icon.png', 'icon.ico'] : ['icon.png'])
      .map((name) => join(root, name))
      .find(existsSync) ?? null
  )
}

export function parseHubNotificationPayload(
  event: HubEvent,
  locale: AppLocale = DEFAULT_LOCALE
): HubNotificationPayload | null {
  if (event.kind !== 'notification.requested' || !event.data || typeof event.data !== 'object') return null
  const data = event.data as Record<string, unknown>
  const title = text(locale, data.titleKey, data.params, data.fallbackTitle, data.title)?.trim()
  if (!title) return null
  return {
    kind: typeof data.kind === 'string' ? data.kind : null,
    workspacePath: typeof data.workspacePath === 'string' ? data.workspacePath : event.workspacePath,
    threadId: typeof data.threadId === 'string' ? data.threadId : null,
    title,
    body: text(locale, data.bodyKey, data.params, data.fallbackBody, data.body),
    actionUrl: typeof data.actionUrl === 'string' ? data.actionUrl : null,
    openDesktopOnClick: typeof data.openDesktopOnClick === 'boolean' ? data.openDesktopOnClick : null
  }
}

export function showHubNotification(
  event: HubEvent,
  options: {
    locale: AppLocale
    settings: AppSettings
    isWorkspaceFocused(workspacePath: string): boolean
    open(payload: HubNotificationPayload): void
  }
): boolean {
  const payload = parseHubNotificationPayload(event, options.locale)
  if (!payload || !Notification.isSupported()) return false
  const focused = options.isWorkspaceFocused(payload.workspacePath ?? '')
  if (payload.kind === 'turnCompleted' || payload.kind === 'turnFailed') {
    const mode = resolveTaskCompletionNotificationMode(options.settings)
    if (mode === 'never' || (mode !== 'always' && focused)) return false
  }
  if (
    payload.kind === 'approvalRequested' &&
    (options.settings.notifications?.approvalRequests === false || focused)
  )
    return false
  if (payload.kind === 'inputRequested' && (options.settings.notifications?.questions === false || focused))
    return false
  const notification = new Notification({
    title: payload.title,
    body: payload.body ?? undefined,
    icon: resolveNotificationIconPath() ?? undefined
  })
  notification.on('click', () => options.open(payload))
  notification.show()
  return true
}
