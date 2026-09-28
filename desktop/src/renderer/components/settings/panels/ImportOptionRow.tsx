import { useId, type JSX, type ReactNode } from 'react'
import { Info } from 'lucide-react'
import { ActionTooltip } from '../../ui/ActionTooltip'
import { Checkbox } from '../../ui/Checkbox'
import { DisclosureChevron } from '../../ui/DisclosureChevron'
import { IconButton } from '../../ui/IconButton'
import styles from './ImportDialog.module.css'

interface ImportOptionRowProps {
  icon: ReactNode
  nested?: boolean
  title: string
  description?: string | null
  info?: string
  infoLabel?: string
  expanded?: boolean
  expandLabel?: string
  controlsId?: string
  onToggleExpanded?: () => void
  checked: boolean
  indeterminate?: boolean
  disabled?: boolean
  onChange: (checked: boolean) => void
}

export function ImportOptionRow({
  icon,
  nested = false,
  title,
  description,
  info,
  infoLabel,
  expanded,
  expandLabel,
  controlsId,
  onToggleExpanded,
  checked,
  indeterminate = false,
  disabled = false,
  onChange
}: ImportOptionRowProps): JSX.Element {
  const checkboxId = useId()
  return (
    <div className={styles.option} data-nested={nested || undefined}>
      <span className={nested ? styles.optionTile : styles.optionIcon} aria-hidden="true">{icon}</span>
      <label htmlFor={checkboxId} className={styles.optionText} data-disabled={disabled || undefined}>
        <span className={styles.optionTitle}>{title}</span>
        {description && <span className={styles.optionHint}>{description}</span>}
      </label>
      <span className={styles.optionActions}>
        {info && (
          <ActionTooltip label={info} multiline>
            <IconButton icon={<Info size={15} strokeWidth={1.8} />} label={infoLabel ?? title} size={24} radius={6} />
          </ActionTooltip>
        )}
        {onToggleExpanded && expanded !== undefined && (
          <IconButton
            icon={<DisclosureChevron expanded={expanded} direction="reveal" />}
            label={expandLabel ?? title}
            tooltipLabel={expandLabel ?? title}
            tooltipPlacement="top"
            aria-expanded={expanded}
            aria-controls={controlsId}
            size={24}
            radius={6}
            onClick={onToggleExpanded}
          />
        )}
        <Checkbox
          id={checkboxId}
          checked={checked}
          indeterminate={indeterminate}
          disabled={disabled}
          ariaLabel={title}
          onChange={onChange}
        />
      </span>
    </div>
  )
}
