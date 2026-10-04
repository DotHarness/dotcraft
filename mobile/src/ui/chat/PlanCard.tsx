import { useState, type ReactNode } from 'react'
import { StyleSheet, View } from 'react-native'
import type { TranscriptEntry } from '../../core/transcript'
import { useI18n } from '../../i18n'
import { Icon } from '../icons'
import { PhoneButton, Txt } from '../parts'
import { SheetHeader, SheetLayer } from '../Sheet'
import { metrics, useTheme } from '../theme'
import { Markdown } from './Markdown'

type PlanEntry = Extract<TranscriptEntry, { kind: 'plan' }>

function Steps({ steps }: { steps: string[] }) {
  return (
    <View style={styles.steps}>
      {steps.map((step, index) => (
        <View key={index} style={styles.step}>
          <Txt tone="secondary" style={styles.number}>{`${index + 1}.`}</Txt>
          <Txt style={styles.shrink}>{step}</Txt>
        </View>
      ))}
    </View>
  )
}

function Section({ label, children }: { label: string; children: ReactNode }) {
  return (
    <View style={styles.section}>
      <Txt variant="meta" tone="dimmed">
        {label}
      </Txt>
      {children}
    </View>
  )
}

function PlanSheet({ entry, title, visible, workspacePath, onClose }: { entry: PlanEntry; title: string; visible: boolean; workspacePath: string | null; onClose: () => void }) {
  const { t } = useI18n()
  return (
    <SheetLayer visible={visible} onClose={onClose}>
      <SheetHeader title={title} onClose={onClose} />
      {entry.overview ? (
        <Section label={t('plan.overview')}>
          <Txt tone="secondary">{entry.overview}</Txt>
        </Section>
      ) : null}
      {entry.steps.length > 0 ? (
        <Section label={t('plan.steps')}>
          <Steps steps={entry.steps} />
        </Section>
      ) : null}
      {entry.content ? (
        <Section label={t('plan.content')}>
          <Markdown text={entry.content} workspacePath={workspacePath} />
        </Section>
      ) : null}
    </SheetLayer>
  )
}

export function PlanCard({ entry, workspacePath }: { entry: PlanEntry; workspacePath: string | null }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const [open, setOpen] = useState(false)
  const title = entry.title || t('plan.badge')
  return (
    <View style={[styles.card, { borderColor: colors.borderDefault, backgroundColor: colors.bgSecondary }]}>
      <View style={styles.badge}>
        <Icon name="listTodo" size={15} color={colors.textSecondary} />
        <Txt variant="meta" tone="secondary" style={styles.medium}>
          {t('plan.badge')}
        </Txt>
      </View>
      <Txt accessibilityRole="header" style={styles.title}>
        {title}
      </Txt>
      {entry.overview ? (
        <Txt tone="secondary" numberOfLines={3}>
          {entry.overview}
        </Txt>
      ) : null}
      {entry.steps.length > 0 ? <Steps steps={entry.steps} /> : null}
      <PhoneButton compact variant="outline" style={styles.view} onPress={() => setOpen(true)}>
        {t('plan.view')}
      </PhoneButton>
      <PlanSheet entry={entry} title={title} visible={open} workspacePath={workspacePath} onClose={() => setOpen(false)} />
    </View>
  )
}

const styles = StyleSheet.create({
  shrink: { flexShrink: 1 },
  medium: { fontWeight: '500' },
  card: { gap: 8, padding: 14, borderWidth: 1, borderRadius: metrics.noticeRadius },
  badge: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  title: { fontWeight: '600', lineHeight: 22 },
  steps: { gap: 4 },
  step: { flexDirection: 'row', gap: 8 },
  number: { minWidth: 18 },
  view: { alignSelf: 'flex-start', marginTop: 4 },
  section: { gap: 6 },
})
