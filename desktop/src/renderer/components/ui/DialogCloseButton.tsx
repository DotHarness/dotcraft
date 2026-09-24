import type { CSSProperties } from 'react'
import { X } from 'lucide-react'
import { IconButton } from './IconButton'

export function DialogCloseButton({ label, onClose, style }: { label: string; onClose: () => void; style?: CSSProperties }): JSX.Element {
  return <IconButton icon={<X size={16} aria-hidden />} label={label} size={30} onClick={onClose} style={style} />
}
