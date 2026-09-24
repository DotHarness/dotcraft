import type { JSX } from 'react'
import { Plus } from 'lucide-react'
import { COMPOSER_FOOTER_CONTROL_HEIGHT, composerFooterControlBoxStyle } from './ComposerShell'
import { ActionTooltip } from '../ui/ActionTooltip'

interface ComposerCommandTriggerProps {
  label: string
  expanded: boolean
  active: boolean
  onClick: () => void
  disabled?: boolean
}

/** Footer trigger that opens the composer's existing slash-command picker. */
export function ComposerCommandTrigger({
  label,
  expanded,
  active,
  onClick,
  disabled = false
}: ComposerCommandTriggerProps): JSX.Element {
  return (
    <div style={{ ...composerFooterControlBoxStyle, flexShrink: 0 }}>
      <ActionTooltip label={label} placement="top">
        <button
          type="button"
          className="dc-composer-icon-control"
          aria-label={label}
          aria-haspopup="listbox"
          aria-expanded={expanded}
          data-active={active}
          disabled={disabled}
          onClick={onClick}
          style={{ width: COMPOSER_FOOTER_CONTROL_HEIGHT, height: COMPOSER_FOOTER_CONTROL_HEIGHT }}
        >
          <Plus size={16} aria-hidden />
        </button>
      </ActionTooltip>
    </div>
  )
}
