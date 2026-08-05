import type {
  AppServerRequestMethod,
  KnownNotificationPayload,
  RawNotificationPayload,
  TypedAppServerRequestApi
} from '../src/shared/appServerBoundary'

declare const appServer: TypedAppServerRequestApi
declare const notification: KnownNotificationPayload

void appServer.sendRequest('thread/read', { threadId: 'thread-1' })

// @ts-expect-error Known calls cannot bypass the generated method map.
void appServer.sendRequest('thread/reed', { threadId: 'thread-1' })

const knownMethod: AppServerRequestMethod = 'turn/start'
void knownMethod

// @ts-expect-error Misspelled methods are rejected at the typed boundary.
const misspelledMethod: AppServerRequestMethod = 'turn/strat'
void misspelledMethod

if (notification.method === 'thread/renamed') {
  notification.params.threadId?.toUpperCase()
  notification.params.displayName?.toUpperCase()
}

const rawNotification: RawNotificationPayload = {
  method: 'third-party/custom-notification',
  params: { open: true }
}
void rawNotification
