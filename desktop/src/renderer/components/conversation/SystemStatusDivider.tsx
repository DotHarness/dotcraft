import { ChevronsDown, Plug, type LucideIcon } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { NoticeDivider } from './NoticeDivider'

const ICONS: Record<string, LucideIcon> = {
  'systemStatus.connectingApps': Plug
}

export function SystemStatusDivider({ labelKey }: { labelKey: string }): JSX.Element {
  const t = useT()
  const label = t(labelKey)
  const Icon = ICONS[labelKey] ?? ChevronsDown
  return <NoticeDivider ariaLabel={label} title={label} icon={<Icon size={14} aria-hidden />} active />
}
