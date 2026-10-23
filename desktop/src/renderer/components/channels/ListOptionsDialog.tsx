import { useId, useRef, useState, type JSX } from 'react'
import { createPortal } from 'react-dom'
import { ListChecks } from 'lucide-react'
import type { ConfigFieldOptionGroupWire, ConfigFieldOptionWire } from '../../../shared/channelModules'
import { LayerBoundary } from '../../contexts/LayerContext'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { Button } from '../ui/Button'
import { Checkbox } from '../ui/Checkbox'
import { DisclosureChevron } from '../ui/DisclosureChevron'
import { IconButton } from '../ui/IconButton'
import { ModalHeader } from '../ui/ModalHeader'
import { useImportDialogFocus } from '../settings/panels/useImportDialogFocus'
import { groupChoice, knownValues, toggleGroup, toggleValue, unknownValues } from './listOptions'
import styles from './ListOptionsDialog.module.css'

export type OptionAvailability =
  | { status: 'loading' }
  | { status: 'known'; available: ReadonlySet<string> }
  | { status: 'unknown' }

interface ListOptionsDialogProps {
  title: string
  description?: string
  platform: string
  options: ConfigFieldOptionWire[]
  optionGroups?: ConfigFieldOptionGroupWire[]
  saved: string[]
  availability: OptionAvailability
  onApply: (values: string[]) => void
  onClose: () => void
}

export function ListOptionsDialog({
  title,
  description,
  platform,
  options,
  optionGroups,
  saved,
  availability,
  onApply,
  onClose
}: ListOptionsDialogProps): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const titleId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)
  const [chosen, setChosen] = useState(() => knownValues(options, saved))
  const [expanded, setExpanded] = useState<ReadonlySet<string>>(() => new Set())
  const dropped = unknownValues(options, saved)
  const isUnavailable = (value: string): boolean =>
    availability.status === 'known' && !availability.available.has(value)
  const chosenUnavailable = chosen.some(isUnavailable)

  useImportDialogFocus(dialogRef, onClose, false)

  const renderValue = (value: string, nested: boolean): JSX.Element => (
    <li key={value} className={nested ? styles.value : styles.row}>
      <Checkbox
        checked={chosen.includes(value)}
        ariaLabel={value}
        onChange={() => setChosen((current) => toggleValue(options, value, current))}
      />
      <code className={styles.code}>{value}</code>
      {isUnavailable(value) && (
        <span className={styles.unavailable}>
          {t('channels.modules.options.unavailable')}
        </span>
      )}
    </li>
  )

  const renderGroup = (group: ConfigFieldOptionGroupWire): JSX.Element => {
    const label = group.localizedDisplayLabel?.[locale] ?? group.displayLabel
    const hint = group.localizedDescription?.[locale] ?? group.description
    const choice = groupChoice(group.values, chosen)
    const missing = group.values.filter(isUnavailable).length
    const open = expanded.has(group.id)
    return (
      <li key={group.id} className={styles.group}>
        <div className={styles.row}>
          <Checkbox
            checked={choice === 'all'}
            indeterminate={choice === 'partial'}
            ariaLabel={label}
            onChange={() => setChosen((current) => toggleGroup(options, group.values, current))}
          />
          <span className={styles.text}>
            <span className={styles.title}>{label}</span>
            {hint && <span className={styles.hint}>{hint}</span>}
          </span>
          {missing > 0 && (
            <span className={styles.unavailable}>
              {t('channels.modules.options.unavailableCount', { count: missing })}
            </span>
          )}
          <IconButton
            icon={<DisclosureChevron expanded={open} direction="reveal" />}
            label={t('channels.modules.options.show', { group: label })}
            aria-expanded={open}
            onClick={() => setExpanded((current) => {
              const next = new Set(current)
              if (open) next.delete(group.id)
              else next.add(group.id)
              return next
            })}
          />
        </div>
        {open && <ul className={styles.values}>{group.values.map((value) => renderValue(value, true))}</ul>}
      </li>
    )
  }

  const dialog = (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      className={styles.scrim}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <div ref={dialogRef} className={styles.dialog} onMouseDown={(event) => event.stopPropagation()}>
        <ModalHeader
          icon={<ListChecks size={18} />}
          title={title}
          titleId={titleId}
          description={description}
          onClose={onClose}
        />

        <ul className={styles.list} aria-busy={availability.status === 'loading' || undefined}>
          {optionGroups?.length
            ? optionGroups.map(renderGroup)
            : options.map((option) => renderValue(option.value, false))}
        </ul>

        {dropped.length > 0 && (
          <p className={styles.note}>
            {dropped.length === 1
              ? t('channels.modules.options.droppedOne')
              : t('channels.modules.options.dropped', { count: dropped.length })}
          </p>
        )}
        {availability.status === 'unknown' && (
          <p className={styles.note}>{t('channels.modules.options.availabilityUnknown', { platform })}</p>
        )}
        {chosenUnavailable && (
          <p className={styles.note} role="status">{t('channels.modules.options.unavailableHint', { platform })}</p>
        )}

        <div className={styles.footer}>
          <Button variant="secondary" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button variant="primary" onClick={() => onApply(chosen)}>
            {t('channels.modules.options.done')}
          </Button>
        </div>
      </div>
    </div>
  )

  return createPortal(<LayerBoundary>{dialog}</LayerBoundary>, document.body) as JSX.Element
}
