import { useMemo, type CSSProperties } from 'react'
import { translate, type AppLocale } from '../../../shared/locales'
import type { ConversationItem } from '../../types/conversation'
import type { FileDiff } from '../../types/toolCall'
import { getSkillManageDisplay } from '../../utils/skillManageToolDisplay'
import { InlineDiffView } from './InlineDiffView'
import { SkillToolCard } from './SkillToolCard'

interface SkillManageCardProps {
  item: ConversationItem
  locale: AppLocale
  diff: FileDiff | null
}

export function SkillManageCard({ item, locale, diff }: SkillManageCardProps): JSX.Element | null {
  const display = useMemo(
    () => getSkillManageDisplay(item.arguments, item.result),
    [item.arguments, item.result]
  )

  if (!display.result?.success || !display.name) return null

  const badge = display.variantUpdated
    ? translate(locale, 'skillManage.card.variantUpdatedBadge')
    : actionBadge(display.action, locale)
  const subtitle = display.message || translate(locale, 'skillManage.card.updatedFallback')

  return (
    <SkillToolCard
      locale={locale}
      skillName={display.name}
      badge={badge}
      subtitle={subtitle}
      showVariantBadge={display.variantUpdated}
    >
      {diff && (
        <div style={diffFrame}>
          <InlineDiffView
            diff={diff}
            variant="embedded"
            showStreamingIndicator={false}
            headerMode="compact"
          />
        </div>
      )}
    </SkillToolCard>
  )
}

function actionBadge(action: ReturnType<typeof getSkillManageDisplay>['action'], locale: AppLocale): string {
  switch (action) {
    case 'create':
      return translate(locale, 'skillManage.card.createdBadge')
    case 'edit':
      return translate(locale, 'skillManage.card.updatedBadge')
    case 'patch':
      return translate(locale, 'skillManage.card.patchedBadge')
    case 'write_file':
      return translate(locale, 'skillManage.card.fileAddedBadge')
    default:
      return translate(locale, 'skillManage.card.updatedBadge')
  }
}

const diffFrame: CSSProperties = {
  marginTop: '2px',
  border: '1px solid var(--border-default)',
  borderRadius: '6px',
  overflow: 'hidden'
}
