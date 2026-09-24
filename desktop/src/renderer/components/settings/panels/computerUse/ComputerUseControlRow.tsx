import type { JSX, ReactNode } from 'react'

import { Button } from '../../../ui/Button'
import { PillSwitch } from '../../../ui/PillSwitch'
import { SettingsRow } from '../../SettingsGroup'
import { settingsHintStyle, settingsLabelStyle } from '../../settingsTypography'
import styles from './ComputerUsePanel.module.css'

interface ComputerUseControlRowProps {
  icon: ReactNode
  name: string
  detail: ReactNode
  manage?: { label: string; onClick: () => void }
  onOpen?: () => void
  checked: boolean
  busy: boolean
  toggleLabel: string
  onToggle: (next: boolean) => void
}

export function ComputerUseControlRow({
  icon,
  name,
  detail,
  manage,
  onOpen,
  checked,
  busy,
  toggleLabel,
  onToggle
}: ComputerUseControlRowProps): JSX.Element {
  const identity = (
    <>
      {icon}
      <div className={styles.controlText}>
        <strong style={settingsLabelStyle()}>{name}</strong>
        <span className={styles.controlDetail} style={settingsHintStyle()}>{detail}</span>
      </div>
    </>
  )
  return (
    <SettingsRow>
      <div className={styles.controlRow}>
        {onOpen
          ? <button type="button" className={styles.controlOpen} onClick={onOpen}>{identity}</button>
          : identity}
        <div className={styles.controlActions}>
          {manage && <Button onClick={manage.onClick}>{manage.label}</Button>}
          <PillSwitch
            checked={checked}
            disabled={busy}
            aria-busy={busy}
            aria-label={toggleLabel}
            onChange={onToggle}
          />
        </div>
      </div>
    </SettingsRow>
  )
}
