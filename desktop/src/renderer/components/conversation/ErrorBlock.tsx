import type { ReactNode } from 'react'
import { CircleAlert } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { CopyButton } from '../ui/CopyButton'
import styles from './ErrorBlock.module.css'

interface ErrorBlockProps {
  message: string
  copyText?: string
  action?: ReactNode
}

/** Error block for turn/failed or error items. Spec §10.3.3 / §18.3. */
export function ErrorBlock({ message, copyText = message, action }: ErrorBlockProps): JSX.Element {
  const t = useT()

  return (
    <div role="alert" className={styles.block}>
      <div className={styles.body}>
        <CircleAlert size={14} aria-hidden className={styles.icon} />
        <span className={styles.message}>{message}</span>
      </div>
      <div className={styles.actions}>
        {action ?? (
          <CopyButton
            key={copyText}
            getText={() => copyText}
            label={t('error.copyAria')}
            copiedLabel={t('error.copiedAria')}
          />
        )}
      </div>
    </div>
  )
}
