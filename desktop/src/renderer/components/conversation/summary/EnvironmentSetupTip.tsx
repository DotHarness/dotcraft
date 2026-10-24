import type { MenuEntry } from '@dotcraft/plugin'
import type { JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { DesktopPluginMenu as Menu } from '../../desktopPlugins/DesktopPluginMenu'
import { Button } from '../../ui/Button'
import styles from './EnvironmentSetupTip.module.css'

export function EnvironmentSetupTip({ items, onDismiss }: {
  items: readonly MenuEntry[]
  onDismiss: () => void
}): JSX.Element {
  const t = useT()
  return (
    <li className={styles.tip}>
      <span className={styles.title}>{t('summary.setupTip.title')}</span>
      <span className={styles.description}>{t('summary.setupTip.description')}</span>
      <span className={styles.actions}>
        <Menu items={items}>
          <Button variant="outline" size="sm">{t('summary.setupTip.action')}</Button>
        </Menu>
        <Button variant="ghost" size="sm" onClick={onDismiss}>{t('summary.setupTip.dismiss')}</Button>
      </span>
    </li>
  )
}
