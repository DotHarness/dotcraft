import { RefreshCw } from 'lucide-react'
import { NoticeDivider } from './NoticeDivider'
import { useLocale } from '../../contexts/LocaleContext'
import { translate } from '../../../shared/locales'
import type { StreamRetryStatus } from '../../stores/conversationStore'

export function StreamRetryNotice({ status }: { status: StreamRetryStatus }): JSX.Element {
  const locale = useLocale()
  const key = status.serverBusy
    ? 'conversation.streamRetry.serverBusy'
    : 'conversation.streamRetry.reconnecting'
  const label = translate(locale, key, {
    attempt: status.attempt ?? 1,
    max: status.max ?? 1
  })

  return (
    <NoticeDivider
      ariaLabel={label}
      title={label}
      icon={<RefreshCw size={12} aria-hidden />}
      active
    />
  )
}
