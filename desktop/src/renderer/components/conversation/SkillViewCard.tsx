import { useMemo } from 'react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { ConversationItem } from '../../types/conversation'
import { getSkillViewDisplay } from '../../utils/skillViewToolDisplay'
import { SkillToolCard } from './SkillToolCard'

interface SkillViewCardProps {
  item: ConversationItem
  locale: AppLocale
}

export function SkillViewCard({ item, locale }: SkillViewCardProps): JSX.Element | null {
  const display = useMemo(
    () => getSkillViewDisplay(item.arguments, item.result),
    [item.arguments, item.result]
  )

  if (!display.loaded || !display.name) return null

  return (
    <SkillToolCard
      locale={locale}
      skillName={display.name}
      badge={translate(locale, 'skillView.card.loadedBadge')}
      subtitle={translate(locale, 'skillView.card.loadedFallback')}
    />
  )
}
