import { useLocalSearchParams } from 'expo-router'
import { ProjectScreen } from '../../ui/screens/ProjectScreen'

export default function Project() {
  const { projectId } = useLocalSearchParams<{ projectId: string }>()
  return <ProjectScreen projectId={projectId} />
}
