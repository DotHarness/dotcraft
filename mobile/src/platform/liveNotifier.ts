import { File, Paths } from 'expo-file-system'
import { AppRegistry, PermissionsAndroid, Platform } from 'react-native'
import LiveSession from '../../modules/live-session'
import { isLive } from '../core/chatState'
import type { LiveFocus, LiveNotice, LiveNotifier, LiveStatus } from '../core/liveSession'
import type { ChatSummary, PendingRequest } from '../core/state'
import { deviceI18n, type I18n } from '../i18n'
import { approvalTitle, subjectOf } from '../ui/chat/approvalText'
import { runningTasksLabel } from '../ui/chat/BackgroundTasks'
import { toolText } from '../ui/chat/Transcript'
import { STATE_LABEL } from '../ui/parts'
import { chatTitle } from '../ui/rows'

// The native live session keeps this task open to run JS timers in the background, and finishes it when the session stops.
AppRegistry.registerHeadlessTask('DotCraftLiveSession', () => () => new Promise<void>(() => undefined))

function chatUrl(computerId: string, { projectId, threadId }: ChatSummary): string {
  return `dotcraft://chat/${encodeURIComponent(computerId)}/${encodeURIComponent(projectId)}/${encodeURIComponent(threadId)}`
}

function requestText({ t }: I18n, request: PendingRequest): string {
  if (request.kind === 'question') return request.questions[0]?.question || t('state.needsAnswer')
  return `${approvalTitle(request, t)} ${subjectOf(request)}`
}

function focusText(i18n: I18n, focus: LiveFocus): string {
  const { t } = i18n
  if (focus.request) return requestText(i18n, focus.request)
  if (!isLive(focus.state) && focus.tasks > 0) return runningTasksLabel(t, focus.tasks)
  switch (focus.activity?.kind) {
    case 'tool':
      return toolText(focus.activity.verb, focus.activity.subject, t)
    case 'thinking':
      return t('chat.thinking')
    case 'replying':
      return t('live.replying')
    default:
      return t(STATE_LABEL[focus.state])
  }
}

function ongoing(i18n: I18n, status: LiveStatus) {
  const { t } = i18n
  const { focus } = status
  const parts: string[] = []
  if (status.running > 0) parts.push(t('live.running', { count: status.running }))
  if (status.needsYou > 0) parts.push(t(status.needsYou === 1 ? 'live.needsYouOne' : 'live.needsYouMany', { count: status.needsYou }))
  const text = !status.reachable ? t('status.connecting') : focus ? focusText(i18n, focus) : t('live.idle')
  return {
    title: focus ? chatTitle(focus.chat, t('chat.untitled')) : status.computer,
    text,
    subText: status.running + status.needsYou > 1 ? parts.join(' · ') : status.computer,
    chip: !status.reachable ? t('status.connecting') : focus ? t(STATE_LABEL[isLive(focus.state) ? focus.state : 'running']) : '',
    url: focus ? chatUrl(focus.computerId, focus.chat) : '',
    end: t('live.end'),
  }
}

function noticeText(i18n: I18n, notice: LiveNotice): string {
  if (notice.kind === 'turnEnded') return i18n.t(notice.failed ? 'state.failed' : 'state.done')
  return requestText(i18n, notice.request)
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
    const channels = { session: i18n.t('live.channelSession'), requests: i18n.t('live.channelRequests'), results: i18n.t('live.channelResults') }
    return LiveSession.notificationsEnabled() && LiveSession.start(ongoing(i18n, status), channels)
  },
  update: (status) => LiveSession.update(ongoing(deviceI18n(), status)),
  stop: () => LiveSession.stop(),
  post(notice) {
    const i18n = deviceI18n()
    const key = `${notice.computerId}/${notice.chat.key}`
    const approval = notice.kind === 'request' && notice.request.kind === 'approval' ? notice.request : null
    LiveSession.post({
      id: notice.id,
      channel: notice.kind === 'request' ? 'requests' : 'results',
      title: chatTitle(notice.chat, i18n.t('chat.untitled')),
      text: noticeText(i18n, notice),
      subText: notice.computer ?? '',
      url: chatUrl(notice.computerId, notice.chat),
      alert: notice.kind === 'request' ? notice.alert : true,
      ...(approval ? { key, requestId: approval.requestId, allow: i18n.t('approval.allowOnce'), reject: i18n.t('approval.reject') } : {}),
    })
  },
  cancel: (id) => LiveSession.cancel(id),
  subscribe(listener) {
    const subscription = LiveSession.addListener('action', ({ action, key, requestId }) => {
      if (action === 'end') listener({ type: 'end' })
      else if (key && requestId) {
        const index = key.indexOf('/')
        listener({ type: action, computerId: key.slice(0, index), key: key.slice(index + 1), requestId })
      }
    })
    return () => subscription.remove()
  },
}
