import { useLocalSearchParams } from 'expo-router'
import { NewChatScreen } from '../../ui/screens/ChatScreen'

export default function NewChat() {
  const { projectId } = useLocalSearchParams<{ projectId: string }>()
  return <NewChatScreen projectId={projectId} />
}
