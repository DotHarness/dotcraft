import { useLocalSearchParams } from 'expo-router'
import { ComputerProvider } from '../../../app-state/SessionContext'
import { ProjectScreen } from '../../../ui/screens/ProjectScreen'

export default function Project() {
  const { computerId, projectId, compose } = useLocalSearchParams<{ computerId: string; projectId: string; compose?: string }>()
  return (
    <ComputerProvider computerId={computerId}>
      <ProjectScreen projectId={projectId} compose={compose === '1'} />
    </ComputerProvider>
  )
}
