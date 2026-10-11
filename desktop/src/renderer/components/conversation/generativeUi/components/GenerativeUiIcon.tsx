import { DynamicIcon, iconNames, type IconName } from 'lucide-react/dynamic'
import { GenerativeUiNodeError } from '../GenerativeUiNodeError'

const ICON_NAMES = new Set<string>(iconNames)

export default function GenerativeUiIcon({ name, size, label }: { name: string; size: number; label?: string }): JSX.Element {
  if (!ICON_NAMES.has(name)) return <GenerativeUiNodeError component="Icon" detail={`"${name}" is not a Lucide icon name.`} />
  return <DynamicIcon name={name as IconName} size={size} strokeWidth={1.75} aria-hidden={label ? undefined : true} aria-label={label} role={label ? 'img' : undefined} />
}
