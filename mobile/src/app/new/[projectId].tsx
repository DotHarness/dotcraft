import { useLocalSearchParams } from 'expo-router'
import { NewChatScreen } from '../../ui/screens/ChatScreen'

export default function NewChat() {
  const { projectId, focus } = useLocalSearchParams<{ projectId: string; focus?: string }>()
  return <NewChatScreen projectId={projectId} focus={focus === '1'} />
}
