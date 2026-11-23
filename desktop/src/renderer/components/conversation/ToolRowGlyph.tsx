import type { JSX } from 'react'
import type { ToolRowIcon } from '../../utils/toolCallDisplay'
import { ToolActivityGlyph } from './ToolActivityGlyph'

export function ToolRowGlyph({ icon, live = false }: { icon: ToolRowIcon; live?: boolean }): JSX.Element {
  if (live && icon.motion) return <ToolActivityGlyph className="dc-tool-row-icon" motion={icon.motion} />
  return <icon.icon className="dc-tool-row-icon" data-tone={icon.tone} size={16} strokeWidth={1.6} aria-hidden />
}
