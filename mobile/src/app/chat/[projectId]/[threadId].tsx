import { useLocalSearchParams } from 'expo-router'
import { ChatScreen } from '../../../ui/screens/ChatScreen'

export default function Chat() {
  const { projectId, threadId } = useLocalSearchParams<{ projectId: string; threadId: string }>()
  return <ChatScreen projectId={projectId} threadId={threadId} />
}
