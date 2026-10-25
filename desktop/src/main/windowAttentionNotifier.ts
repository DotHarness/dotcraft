import { Notification, type BrowserWindow } from 'electron'
import { DEFAULT_LOCALE, normalizeLocale, translate } from '../shared/locales'
import type { WorkspaceProjectKind } from '../shared/workspaceProjects'
import type { AppSettings } from './settings'
import { isTrayRunning } from './trayLock'

export const NEW_TASK_OFFER_ATTENTION = 'desktop/newTaskOffer'

const ATTENTION_REQUESTS: Record<string, { setting: 'approvalRequests' | 'questions'; bodyKey: string; hubDelivers: boolean }> = {
  'item/approval/request': {
    setting: 'approvalRequests',
    bodyKey: 'hub.notification.approval_requested.body',
    hubDelivers: true
  },
  'item/tool/requestUserInput': {
    setting: 'questions',
    bodyKey: 'hub.notification.input_requested.body',
    hubDelivers: true
  },
  [NEW_TASK_OFFER_ATTENTION]: {
    setting: 'questions',
    bodyKey: 'hub.notification.new_task_offer.body',
    hubDelivers: false
  }
}

export interface WindowAttentionContext {
  connectionKind: WorkspaceProjectKind
  window: BrowserWindow
  settings: AppSettings
  threads: unknown[]
  openThread: (threadId: string) => void
}

export type WindowAttentionNotifier = (
  method: string,
  params: unknown,
  context: WindowAttentionContext
) => Promise<boolean>

function stringField(record: Record<string, unknown>, key: string): string {
  const value = record[key]
  return typeof value === 'string' ? value.trim() : ''
}

function chatName(threads: unknown[], threadId: string): string {
  for (const thread of threads) {
    if (thread && typeof thread === 'object' && stringField(thread as Record<string, unknown>, 'id') === threadId) {
      return stringField(thread as Record<string, unknown>, 'displayName')
    }
  }
  return ''
}

export function createWindowAttentionNotifier(
  trayRunning: () => Promise<boolean> = () => isTrayRunning()
): WindowAttentionNotifier {
  const seen = new Set<string>()
  return async (method, params, context) => {
    const request = ATTENTION_REQUESTS[method]
    if (!request || !params || typeof params !== 'object') return false
    const fields = params as Record<string, unknown>
    const threadId = stringField(fields, 'threadId')
    const key = [threadId, stringField(fields, 'turnId'), stringField(fields, 'requestId')].join('\n')
    if (seen.has(key)) return false
    seen.add(key)

    if (context.settings.notifications?.[request.setting] === false || context.window.isFocused()) return false
    if (request.hubDelivers && context.connectionKind !== 'remote' && await trayRunning()) return false
    if (!Notification.isSupported()) return false

    const notification = new Notification({
      title: chatName(context.threads, threadId) || 'DotCraft',
      body: translate(normalizeLocale(context.settings.locale ?? DEFAULT_LOCALE), request.bodyKey)
    })
    notification.on('click', () => context.openThread(threadId))
    notification.show()
    return true
  }
}
