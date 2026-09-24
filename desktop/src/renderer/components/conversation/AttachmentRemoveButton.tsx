import { X } from 'lucide-react'
import { IconButton } from '../ui/IconButton'

export function AttachmentRemoveButton({ label, onRemove }: { label: string; onRemove: () => void }): JSX.Element {
  return (
    <IconButton
      icon={<X size={12} aria-hidden />}
      size={18}
      radius={9}
      label={label}
      tooltipLabel={label}
      tooltipPlacement="top"
      onClick={onRemove}
    />
  )
}
