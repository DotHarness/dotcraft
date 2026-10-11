import { app, type IpcMainInvokeEvent } from 'electron'
import type { ChatsFolderInfo } from '../shared/chatFolders'
import { createChatFolder, knownChatsFolderRoots, normalizeChatsFolderSetting, resolveChatsFolderRoot } from './chatFolders'
import type { AppSettings } from './settings'

type HandleSafe = (
  channel: string,
  listener: (event: IpcMainInvokeEvent, ...args: unknown[]) => unknown
) => void

export const CHAT_FOLDERS_CHANNELS = ['workspace:get-chats-folder', 'workspace:create-chat-folder'] as const

export function chatsFolderInfo(settings: AppSettings): ChatsFolderInfo {
  return {
    path: resolveChatsFolderRoot(settings.chatsFolder, app.getPath('documents')),
    customized: normalizeChatsFolderSetting(settings.chatsFolder) !== undefined
  }
}

export function knownChatsFolderRootsFromSettings(settings: AppSettings): string[] {
  return knownChatsFolderRoots(settings, app.getPath('documents'))
}

export function createChatFolderFromSettings(settings: AppSettings, firstMessage: string): Promise<string> {
  return createChatFolder(chatsFolderInfo(settings).path, firstMessage)
}

export function registerChatFoldersHandlers(handleSafe: HandleSafe, getSettings: () => AppSettings): void {
  handleSafe('workspace:get-chats-folder', () => chatsFolderInfo(getSettings()))
  handleSafe('workspace:create-chat-folder', async (_event, params) => {
    const firstMessage = (params as { firstMessage?: unknown } | undefined)?.firstMessage
    const path = await createChatFolderFromSettings(
      getSettings(),
      typeof firstMessage === 'string' ? firstMessage : ''
    )
    return { path }
  })
}
