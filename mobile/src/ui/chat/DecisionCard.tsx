import { StyleSheet, View } from 'react-native'
import type { Decision } from '../../core/decisions'
import { useI18n } from '../../i18n'
import { Txt } from '../parts'
import { useTheme } from '../theme'
import { ApprovalBody, PlanBody, QuestionBody, type DecisionActions } from './DecisionBodies'

export function DecisionCard({ decision, count, disabled, actions }: { decision: Decision; count: number; disabled: boolean; actions: DecisionActions }) {
  const { t } = useI18n()
  const { colors } = useTheme()

  return (
    <View style={[styles.card, { backgroundColor: colors.composerInputBackground, borderColor: colors.composerInputBorder }]}>
      {count > 1 ? (
        <Txt variant="meta" tone="dimmed" style={styles.count}>
          {t('decision.count', { index: 1, count })}
        </Txt>
      ) : null}
      {decision.kind === 'approval' ? (
        <ApprovalBody request={decision} disabled={disabled} actions={actions} />
      ) : decision.kind === 'question' ? (
        <QuestionBody request={decision} disabled={disabled} actions={actions} />
      ) : (
        <PlanBody disabled={disabled} actions={actions} />
      )}
    </View>
  )
}

const styles = StyleSheet.create({
  card: { gap: 10, paddingTop: 14, paddingBottom: 10, paddingHorizontal: 12, borderWidth: 1, borderRadius: 26 },
  count: { alignSelf: 'flex-end', marginBottom: -6, paddingHorizontal: 4 },
})
