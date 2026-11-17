import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'

export function useHostFilesUnavailable(): boolean {
  const remote = useConversationStore((s) => s.remoteWorkspaceActive)
  const fileSystem = useConnectionStore((s) => s.capabilities?.fileSystem === true)
  return remote && !fileSystem
}
