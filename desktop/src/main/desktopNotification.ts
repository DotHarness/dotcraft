import { pathToFileURL } from 'node:url'
import { Notification, type NotificationConstructorOptions } from 'electron'
import { buildWorkspaceOpenDeepLink, parseWorkspaceOpenDeepLink } from '../shared/desktopDeepLink'

export interface DesktopNotificationActivation {
  open(): void
  url?: string
}

const LIVE_NOTIFICATION_LIMIT = 128
const liveNotifications = new Set<Notification>()

const xmlEntities: Record<string, string> = {
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&apos;'
}

function escapeXml(value: string): string {
  return value
    .replace(/[^\u0009\u000a\u000d\u0020-\ud7ff\ue000-\ufffd\u{10000}-\u{10ffff}]/gu, '')
    .replace(/[&<>"']/g, (character) => xmlEntities[character])
}

function workspaceToastXml(options: NotificationConstructorOptions, url: string): string {
  const { title, body, icon, silent } = options
  const text = [title, body].map((value) => value ? `<text>${escapeXml(value)}</text>` : '').join('')
  const image = typeof icon === 'string' && icon
    ? `<image placement="appLogoOverride" src="${escapeXml(pathToFileURL(icon).href)}"/>`
    : ''
  return `<toast activationType="protocol" launch="${escapeXml(url)}">`
    + `<visual><binding template="ToastGeneric">${text}${image}</binding></visual>`
    + (silent ? '<audio silent="true"/>' : '')
    + '</toast>'
}

function bindLiveActivation(notification: Notification, activation: DesktopNotificationActivation): void {
  const release = (): void => { liveNotifications.delete(notification) }
  notification.once('click', () => {
    release()
    activation.open()
  })
  notification.once('failed', release)
  notification.on('close', (details) => {
    if (details.reason !== 'timedOut') release()
  })
  liveNotifications.add(notification)
  if (liveNotifications.size > LIVE_NOTIFICATION_LIMIT) {
    const oldest = liveNotifications.values().next()
    if (!oldest.done) liveNotifications.delete(oldest.value)
  }
}

export function createDesktopNotification(
  options: NotificationConstructorOptions,
  activation?: DesktopNotificationActivation
): Notification | null {
  if (!Notification.isSupported()) return null

  const link = process.platform === 'win32' && activation?.url && parseWorkspaceOpenDeepLink(activation.url)
  if (link) {
    const url = buildWorkspaceOpenDeepLink(link.workspacePath, link.threadId)
    return new Notification({ ...options, toastXml: workspaceToastXml(options, url) })
  }

  const notification = new Notification(options)
  if (activation) bindLiveActivation(notification, activation)
  return notification
}
