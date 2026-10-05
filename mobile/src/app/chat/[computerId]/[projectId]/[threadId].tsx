import { useLocalSearchParams } from 'expo-router'
import { ComputerProvider } from '../../../../app-state/SessionContext'
import { ChatScreen } from '../../../../ui/screens/ChatScreen'

export default function Chat() {
  const { computerId, projectId, threadId } = useLocalSearchParams<{ computerId: string; projectId: string; threadId: string }>()
  return (
    <ComputerProvider computerId={computerId}>
      <ChatScreen projectId={projectId} threadId={threadId} />
    </ComputerProvider>
  )
}
