import { RotateCw } from 'lucide-react'
import { useT } from '../../../contexts/LocaleContext'
import { Button } from '../../ui/Button'
import styles from './ViewerNotice.module.css'

export function FileChangedNotice({ onRefresh }: { onRefresh: () => void }): JSX.Element {
  const t = useT()
  return (
    <div className={styles.changed} role="status">
      <span className={styles.changedText}>{t('viewer.fileChanged')}</span>
      <Button size="sm" variant="outline" iconLeft={<RotateCw size={14} aria-hidden />} onClick={onRefresh}>
        {t('viewer.refreshLatest')}
      </Button>
    </div>
  )
}
