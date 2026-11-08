import { useEffect } from 'react'
import { useConversationStore } from '../stores/conversationStore'

export function useConversationWorkspace(workspacePath: string, remoteWorkspaceActive: boolean): void {
  const setWorkspacePath = useConversationStore((state) => state.setWorkspacePath)

  useEffect(() => {
    useConversationStore.setState({ workspacePath, remoteWorkspaceActive })
  }, [workspacePath, remoteWorkspaceActive, setWorkspacePath])
}
