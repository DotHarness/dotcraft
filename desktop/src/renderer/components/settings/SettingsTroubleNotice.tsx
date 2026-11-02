import type { JSX, ReactNode } from 'react'
import { TriangleAlert } from 'lucide-react'
import styles from './SettingsTroubleNotice.module.css'

interface SettingsTroubleNoticeProps {
  message: ReactNode
  action?: { label: string; run: () => void }
}

export function SettingsTroubleNotice({ message, action }: SettingsTroubleNoticeProps): JSX.Element {
  return (
    <div className={styles.notice} role="status">
      <TriangleAlert size={20} aria-hidden="true" />
      <div className={styles.title}>
        {message}
        {action && (
          <>
            {' '}
            <button type="button" className={styles.link} onClick={action.run}>
              {action.label}
            </button>
          </>
        )}
      </div>
    </div>
  )
}
