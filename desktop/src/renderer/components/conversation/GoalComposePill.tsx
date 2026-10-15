import { useState } from 'react'
import { CircleX, Target } from 'lucide-react'
import { ActionTooltip } from '../ui/ActionTooltip'
import { composerModePillStyle } from './ComposerShell'

/** Footer indicator shown while the composer is in goal-compose mode; clicking it exits. */
export function GoalComposePill({
  label,
  title,
  ariaLabel,
  onExit
}: {
  label: string
  title: string
  ariaLabel: string
  onExit: () => void
}): JSX.Element {
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const active = hovered || focused
  const Icon = active ? CircleX : Target

  return (
    <ActionTooltip label={title} placement="top">
      <button
        type="button"
        onClick={onExit}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        onFocus={() => setFocused(true)}
        onBlur={() => setFocused(false)}
        aria-label={ariaLabel}
        className="dc-composer-pill"
        style={composerModePillStyle}
      >
        <Icon size={13} strokeWidth={2} aria-hidden />
        <span>{label}</span>
      </button>
    </ActionTooltip>
  )
}
