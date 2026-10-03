import { useLocalSearchParams } from 'expo-router'
import { PairScanScreen } from '../../ui/screens/PairScreens'

export default function PairScan() {
  const params = useLocalSearchParams()
  return <PairScanScreen params={params} />
}
