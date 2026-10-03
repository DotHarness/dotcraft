import { File, Paths } from 'expo-file-system'
import { AppRegistry, PermissionsAndroid, Platform } from 'react-native'
import LiveSession from '../../modules/live-session'
import type { LiveNotice, LiveNotifier, LiveStatus } from '../core/liveSession'
import { deviceI18n, type I18n } from '../i18n'
import { approvalTitle, subjectOf } from '../ui/chat/RequestCards'
import { chatTitle } from '../ui/rows'

// The native live session keeps this task open to run JS timers in the background, and finishes it when the session stops.
AppRegistry.registerHeadlessTask('DotCraftLiveSession', () => () => new Promise<void>(() => undefined))

function ongoing({ t }: I18n, status: LiveStatus) {
  const parts: string[] = []
  if (status.running > 0) parts.push(t('live.running', { count: status.running }))
  if (status.needsYou > 0) parts.push(t(status.needsYou === 1 ? 'live.needsYouOne' : 'live.needsYouMany', { count: status.needsYou }))
  const text = !status.reachable ? t('status.connecting') : parts.length > 0 ? parts.join(' · ') : t('live.idle')
  return { title: status.computer, text, end: t('live.end') }
}

function noticeText({ t }: I18n, notice: LiveNotice): string {
  if (notice.kind === 'turnEnded') return t(notice.failed ? 'state.failed' : 'state.done')
  const { request } = notice
  if (request.kind === 'question') return request.questions[0]?.question || t('state.needsAnswer')
  return `${approvalTitle(request, t)} ${subjectOf(request)}`
}

export const liveNotifier: LiveNotifier = {
  async prepare() {
    const asked = new File(Paths.document, 'notifications-asked')
    if (Platform.OS !== 'android' || Platform.Version < 33 || asked.exists) return
    asked.create()
    await PermissionsAndroid.request(PermissionsAndroid.PERMISSIONS.POST_NOTIFICATIONS)
  },
  start(status) {
    const i18n = deviceI18n()
    const channels = { session: i18n.t('live.channelSession'), requests: i18n.t('home.needsYou'), results: i18n.t('live.channelResults') }
    return LiveSession.notificationsEnabled() && LiveSession.start(ongoing(i18n, status), channels)
  },
  update: (status) => LiveSession.update(ongoing(deviceI18n(), status)),
  stop: () => LiveSession.stop(),
  post(notice) {
    const i18n = deviceI18n()
    const { key, projectId, threadId } = notice.chat
    const approval = notice.kind === 'request' && notice.request.kind === 'approval' ? notice.request : null
    LiveSession.post({
      id: notice.id,
      channel: notice.kind === 'request' ? 'requests' : 'results',
      title: chatTitle(notice.chat, i18n.t('chat.untitled')),
      text: noticeText(i18n, notice),
      url: `dotcraft://chat/${encodeURIComponent(projectId)}/${encodeURIComponent(threadId)}`,
      alert: notice.kind === 'request' ? notice.alert : true,
      ...(approval ? { key, requestId: approval.requestId, allow: i18n.t('approval.allowOnce'), reject: i18n.t('approval.reject') } : {}),
    })
  },
  cancel: (id) => LiveSession.cancel(id),
  subscribe(listener) {
    const subscription = LiveSession.addListener('action', ({ action, key, requestId }) => {
      if (action === 'end') listener({ type: 'end' })
      else if (key && requestId) listener({ type: action, key, requestId })
    })
    return () => subscription.remove()
  },
}
