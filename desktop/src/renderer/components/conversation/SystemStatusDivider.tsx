import { Archive, ChevronsDown } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { NoticeDivider } from './NoticeDivider'

export function SystemStatusDivider({ labelKey }: { labelKey: string }): JSX.Element {
  const t = useT()
  const label = t(labelKey)
  const Icon = labelKey === 'systemStatus.consolidating' ? Archive : ChevronsDown
  return <NoticeDivider ariaLabel={label} title={label} icon={<Icon size={12} aria-hidden />} active />
}
