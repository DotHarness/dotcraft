import { ipcRenderer } from 'electron'
import { CREATOR_MODE_LIST_CHANNEL, CREATOR_MODE_SET_CHANNEL } from '../shared/creatorMode'

export const creatorModePreload = {
  listCreatorThreads(): Promise<string[]> {
    return ipcRenderer.invoke(CREATOR_MODE_LIST_CHANNEL)
  },
  setCreatorThread(threadId: string, creator: boolean): Promise<void> {
    return ipcRenderer.invoke(CREATOR_MODE_SET_CHANNEL, { threadId, creator })
  }
}
