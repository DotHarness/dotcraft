import { Info } from 'lucide-react'
import { ErrorBlock } from './ErrorBlock'
import { NoticeDivider } from './NoticeDivider'
import { useLocale } from '../../contexts/LocaleContext'
import { translate } from '../../../shared/locales'

interface TurnFailureNoticeProps {
  message: string
  providerError?: string
}

export function TurnFailureNotice({ message, providerError }: TurnFailureNoticeProps): JSX.Element {
  const locale = useLocale()

  if (providerError === 'usageLimitExceeded') {
    const label = translate(locale, 'conversation.providerFailure.usageLimit')
    return <NoticeDivider ariaLabel={label} title={label} icon={<Info size={14} aria-hidden />} />
  }

  if (providerError === 'serverOverloaded' || providerError === 'rateLimitExceeded') {
    return <ErrorBlock message={translate(locale, 'errors.serverOverloaded')} />
  }

  return <ErrorBlock message={message} />
}
