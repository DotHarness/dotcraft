import { useState } from 'react'
import { ScrollView, StyleSheet, Text, useWindowDimensions, View } from 'react-native'
import type { Decision } from '../../core/decisions'
import { useI18n, type I18n } from '../../i18n'
import { PhoneButton, RoundIconButton, Txt } from '../parts'
import { metrics, type, useTheme } from '../theme'
import { approvalTitle, subjectOf } from './approvalText'
import { ApprovalBody, PlanBody, QuestionBody, type DecisionActions } from './DecisionBodies'

function heading(decision: Decision, t: I18n['t'], planTitle: string | null): { title: string; summary: string } {
  if (decision.kind === 'approval') return { title: approvalTitle(decision, t), summary: subjectOf(decision).split(/\r?\n/, 1)[0] }
  if (decision.kind === 'question') return { title: t('question.label'), summary: decision.questions[0]?.question ?? '' }
  return { title: t('plan.title'), summary: planTitle ?? '' }
}

export function DecisionDrawer({
  decision,
  count,
  disabled,
  planTitle,
  actions,
}: {
  decision: Decision
  count: number
  disabled: boolean
  planTitle: string | null
  actions: DecisionActions
}) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const { height } = useWindowDimensions()
  const [minimized, setMinimized] = useState(false)
  const { title, summary } = heading(decision, t, planTitle)
  const surface = { backgroundColor: colors.bgElevated, borderColor: colors.borderDefault, boxShadow: colors.shadow3 }

  return (
    <>
      {minimized ? (
        <View style={[styles.bar, surface]}>
          <View style={styles.barText}>
            <Txt numberOfLines={1} style={styles.strong}>
              {title}
            </Txt>
            {summary ? (
              <Text numberOfLines={1} style={[decision.kind === 'approval' && decision.approvalType === 'shell' ? type.code : type.meta, { color: colors.textSecondary }]}>
                {summary}
              </Text>
            ) : null}
          </View>
          <PhoneButton compact variant="outline" onPress={() => setMinimized(false)}>
            {t('decision.review')}
          </PhoneButton>
        </View>
      ) : null}
      <View accessibilityLabel={title} style={[styles.card, surface, minimized && styles.hidden]}>
        <View style={styles.head}>
          <Txt accessibilityRole="header" style={[type.sheetTitle, styles.title]}>
            {title}
          </Txt>
          {count > 1 ? (
            <Txt variant="meta" tone="secondary">
              {t('decision.count', { index: 1, count })}
            </Txt>
          ) : null}
          <RoundIconButton label={t('decision.minimize')} icon="chevronDown" onPress={() => setMinimized(true)} />
        </View>
        <ScrollView
          style={{ maxHeight: height * 0.55 }}
          contentContainerStyle={styles.body}
          keyboardShouldPersistTaps="handled"
          showsVerticalScrollIndicator={false}
        >
          {decision.kind === 'approval' ? (
            <ApprovalBody request={decision} disabled={disabled} actions={actions} />
          ) : decision.kind === 'question' ? (
            <QuestionBody request={decision} disabled={disabled} actions={actions} />
          ) : (
            <PlanBody disabled={disabled} actions={actions} />
          )}
        </ScrollView>
      </View>
    </>
  )
}

const styles = StyleSheet.create({
  hidden: { display: 'none' },
  strong: { fontWeight: '600' },
  card: { gap: 12, paddingTop: 12, paddingBottom: 14, paddingHorizontal: 14, borderWidth: 1, borderRadius: metrics.heroRadius },
  head: { flexDirection: 'row', alignItems: 'center', gap: 10 },
  title: { flex: 1, minWidth: 0 },
  body: { gap: 12 },
  bar: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 12,
    paddingVertical: 10,
    paddingLeft: 16,
    paddingRight: 10,
    borderWidth: 1,
    borderRadius: metrics.heroRadius,
  },
  barText: { flex: 1, minWidth: 0, gap: 1 },
})
