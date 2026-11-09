import { useState } from 'react'
import { Pressable, StyleSheet, View } from 'react-native'
import type { QuestionAnswer } from '../../core/transcriptTypes'
import { useI18n } from '../../i18n'
import { Icon } from '../icons'
import { Txt } from '../parts'
import { useTheme } from '../theme'

export function QuestionToolRow({ questions }: { questions: QuestionAnswer[] }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const [open, setOpen] = useState(false)
  const label = t(questions.length === 1 ? 'question.asked.one' : 'question.asked.many', { count: questions.length })
  return (
    <View style={styles.block}>
      <Pressable accessibilityRole="button" accessibilityState={{ expanded: open }} onPress={() => setOpen((value) => !value)} style={styles.row}>
        <Icon name="messageCircleQuestionMark" size={16} color={colors.textSecondary} strokeWidth={1.6} />
        <Txt tone="secondary" numberOfLines={1} style={styles.label}>{label}</Txt>
        <Icon name={open ? 'chevronDown' : 'chevronRight'} size={14} color={colors.textSecondary} />
      </Pressable>
      {open ? (
        <View style={styles.answers}>
          {questions.map((question) => (
            <View key={question.id} style={styles.answer}>
              <Txt selectable tone="secondary" style={styles.text}>{question.question}</Txt>
              <Txt selectable style={styles.text}>
                {question.hidden ? t('question.hiddenAnswer') : question.answers.length > 0 ? question.answers.join(', ') : t('question.noAnswer')}
              </Txt>
            </View>
          ))}
        </View>
      ) : null}
    </View>
  )
}

const styles = StyleSheet.create({
  block: { gap: 8 },
  row: { flexDirection: 'row', alignItems: 'center', gap: 6, minWidth: 0 },
  label: { flexShrink: 1, lineHeight: 22 },
  answers: { gap: 12, paddingLeft: 22 },
  answer: { gap: 4 },
  text: { lineHeight: 22 },
})
