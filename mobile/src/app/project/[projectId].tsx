import { useLocalSearchParams } from 'expo-router'
import { ProjectScreen } from '../../ui/screens/ProjectScreen'

export default function Project() {
  const { projectId, compose } = useLocalSearchParams<{ projectId: string; compose?: string }>()
  return <ProjectScreen projectId={projectId} compose={compose === '1'} />
}
