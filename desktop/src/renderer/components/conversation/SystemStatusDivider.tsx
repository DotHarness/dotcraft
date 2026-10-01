import { ChevronsDown } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { NoticeDivider } from './NoticeDivider'

export function SystemStatusDivider({ labelKey }: { labelKey: string }): JSX.Element {
  const t = useT()
  const label = t(labelKey)
  return <NoticeDivider ariaLabel={label} title={label} icon={<ChevronsDown size={14} aria-hidden />} active />
}
