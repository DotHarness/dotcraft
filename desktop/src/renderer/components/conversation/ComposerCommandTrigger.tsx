import { useState, type JSX } from 'react'
import { Plus } from 'lucide-react'
import {
  COMPOSER_FOOTER_CONTROL_HEIGHT,
  composerFooterControlActiveBackground,
  composerFooterControlBoxStyle,
  composerFooterControlHoverBackground
} from './ComposerShell'
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
  const [hovered, setHovered] = useState(false)

  return (
    <div style={{ ...composerFooterControlBoxStyle, flexShrink: 0 }}>
      <ActionTooltip label={label} placement="top">
        <button
          type="button"
          aria-label={label}
          aria-haspopup="listbox"
          aria-expanded={expanded}
          data-active={active}
          disabled={disabled}
          onMouseEnter={() => setHovered(true)}
          onMouseLeave={() => setHovered(false)}
          onFocus={(event) => {
            if (event.currentTarget.matches(':focus-visible')) setHovered(true)
          }}
          onBlur={() => setHovered(false)}
          onClick={onClick}
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            width: COMPOSER_FOOTER_CONTROL_HEIGHT,
            height: COMPOSER_FOOTER_CONTROL_HEIGHT,
            padding: 0,
            borderRadius: '999px',
            border: 'none',
            background: !disabled
              ? active
                ? composerFooterControlActiveBackground
                : hovered
                  ? composerFooterControlHoverBackground
                  : 'transparent'
              : 'transparent',
            color: disabled ? 'var(--composer-footer-muted)' : 'var(--composer-footer-text)',
            cursor: disabled ? 'default' : 'pointer',
            lineHeight: 1,
            boxSizing: 'border-box',
            transition: 'background-color 120ms ease, color 120ms ease'
          }}
        >
          <Plus size={16} strokeWidth={2} aria-hidden />
        </button>
      </ActionTooltip>
    </div>
  )
}
