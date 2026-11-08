import { CircleAlert } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { Button } from '../ui/Button'

interface Props {
  message: string
  fixPrompt: string
  onRetry?: () => void
}

export function VisualizationNotice({ message, fixPrompt, onRetry }: Props): JSX.Element {
  const t = useT()
  return (
    <div role="status" className="visualization-notice">
      <CircleAlert size={14} aria-hidden className="visualization-notice__icon" />
      <span className="visualization-notice__text">{message}</span>
      {onRetry && <Button size="sm" variant="secondary" onClick={onRetry}>{t('common.retry')}</Button>}
      <Button size="sm" variant="ghost" onClick={() => useUIStore.getState().setComposerPrefill(fixPrompt)}>
        {t('visualization.fixAction')}
      </Button>
    </div>
  )
}
