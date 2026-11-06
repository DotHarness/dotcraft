import { X } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'
import { useFeedbackSurvey, type FeedbackRating, type FeedbackSurveyState } from './useFeedbackSurvey'
import css from './FeedbackSurvey.module.css'

const REPOSITORY_URL = 'https://github.com/DotHarness/dotcraft'
const RATINGS: FeedbackRating[] = ['bad', 'fine', 'good']

export function feedbackFollowUpUrl(rating: FeedbackRating, platform: string): string {
  if (rating === 'good') return REPOSITORY_URL
  const title = `Session feedback: ${rating === 'bad' ? 'Bad' : 'Fine'}`
  const body = `**Rating:** ${rating === 'bad' ? 'Bad' : 'Fine'}\n**OS:** ${platform}\n\n**What could be better?**\n\n`
  return `${REPOSITORY_URL}/issues/new?title=${encodeURIComponent(title)}&body=${encodeURIComponent(body)}`
}

interface FeedbackSurveyRowProps {
  state: FeedbackSurveyState
  onRate: (rating: FeedbackRating) => void
  onFollowUp: (rating: FeedbackRating) => void
  onDismiss: () => void
}

export function FeedbackSurveyRow({ state, onRate, onFollowUp, onDismiss }: FeedbackSurveyRowProps): JSX.Element {
  const t = useT()
  return (
    <div className={css.row}>
      <span className={css.text} role="status" aria-live="polite" aria-atomic="true">
        {t(state.phase === 'ask' ? 'feedbackSurvey.question' : 'feedbackSurvey.thanks')}
      </span>
      <div className={css.actions} role="group" aria-label={state.phase === 'ask' ? t('feedbackSurvey.rateLabel') : undefined}>
        {state.phase === 'ask'
          ? RATINGS.map((rating) => (
              <Button key={rating} variant="ghost" size="sm" onClick={() => onRate(rating)}>
                {t(`feedbackSurvey.rating.${rating}`)}
              </Button>
            ))
          : (
              <Button variant="ghost" size="sm" onClick={() => onFollowUp(state.rating)}>
                {t(state.rating === 'good' ? 'feedbackSurvey.star' : 'feedbackSurvey.tellUsMore')}
              </Button>
            )}
        <IconButton icon={<X size={14} aria-hidden="true" />} label={t('feedbackSurvey.dismiss')} size={24} radius={6} onClick={onDismiss} />
      </div>
    </div>
  )
}

export function FeedbackSurvey({ threadId }: { threadId: string }): JSX.Element | null {
  const { state, rate, dismiss } = useFeedbackSurvey(threadId)
  if (state === null) return null
  return (
    <FeedbackSurveyRow
      state={state}
      onRate={rate}
      onFollowUp={(rating) => {
        void window.api.shell.openExternal(feedbackFollowUpUrl(rating, window.api.platform))
        dismiss()
      }}
      onDismiss={dismiss}
    />
  )
}
