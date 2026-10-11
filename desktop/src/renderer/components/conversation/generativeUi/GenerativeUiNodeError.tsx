import { CircleAlert } from 'lucide-react'
import { useT } from '../../../contexts/LocaleContext'
import styles from './GenerativeUi.module.css'

export function GenerativeUiNodeError({ component, detail }: { component: string; detail: string }): JSX.Element {
  const t = useT()
  const label = t('generativeUi.nodeError', { component })
  return (
    <span role="note" className={styles.nodeError} title={detail}>
      <CircleAlert size={14} aria-hidden />
      <span className={styles.nodeErrorText}>{`${label} ${detail}`}</span>
    </span>
  )
}
