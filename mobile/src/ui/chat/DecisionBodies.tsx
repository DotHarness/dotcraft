import { useState, type ReactNode } from 'react'
import { Pressable, StyleSheet, Text, TextInput, View } from 'react-native'
import { hasOther, initialChoices, questionAnswers, type QuestionChoice } from '../../core/decisions'
import type { ApprovalDecision } from '../../core/projectConnection'
import type { PendingRequest } from '../../core/state'
import { baseName } from '../../core/transcript'
import { useI18n } from '../../i18n'
import { Icon } from '../icons'
import { PhoneButton, Txt } from '../parts'
import { type, useTheme } from '../theme'
import { subjectOf } from './approvalText'

type Approval = Extract<PendingRequest, { kind: 'approval' }>
type Question = Extract<PendingRequest, { kind: 'question' }>

export interface DecisionActions {
  decide: (requestId: string, decision: ApprovalDecision) => void
  answer: (requestId: string, answers: Record<string, string[]>) => void
  dismissQuestion: (request: Question) => Promise<void>
  implement: () => Promise<void>
  feedback: (text: string) => Promise<void>
  dismissPlan: () => void
}

function ChoiceList({ children }: { children: ReactNode }) {
  const { colors } = useTheme()
  return <View style={[styles.choices, { borderColor: colors.borderDefault }]}>{children}</View>
}

function ChoiceRow({
  label,
  description,
  selected,
  first,
  disabled,
  onPress,
}: {
  label: string
  description?: string
  selected?: boolean
  first: boolean
  disabled: boolean
  onPress: () => void
}) {
  const { colors } = useTheme()
  return (
    <Pressable
      accessibilityRole={selected === undefined ? 'button' : 'radio'}
      accessibilityState={selected === undefined ? { disabled } : { checked: selected, disabled }}
      accessibilityLabel={label}
      disabled={disabled}
      onPress={onPress}
      style={({ pressed }) => [
        styles.choice,
        !first && { borderTopWidth: 1, borderTopColor: colors.borderDefault },
        (pressed || selected) && { backgroundColor: colors.bgTertiary },
        disabled && styles.disabled,
      ]}
    >
      <View style={styles.choiceText}>
        <Txt style={styles.strong}>{label}</Txt>
        {description ? (
          <Txt variant="meta" tone="secondary">
            {description}
          </Txt>
        ) : null}
      </View>
      {selected ? <Icon name="check" size={18} color={colors.textPrimary} strokeWidth={2} /> : null}
    </Pressable>
  )
}

function InputRow({
  value,
  placeholder,
  selected,
  secret,
  disabled,
  onFocus,
  onChange,
}: {
  value: string
  placeholder: string
  selected: boolean
  secret: boolean
  disabled: boolean
  onFocus: () => void
  onChange: (value: string) => void
}) {
  const { colors } = useTheme()
  return (
    <View style={[styles.choice, { borderTopWidth: 1, borderTopColor: colors.borderDefault }, selected && { backgroundColor: colors.bgTertiary }]}>
      <TextInput
        value={value}
        editable={!disabled}
        multiline={!secret}
        secureTextEntry={secret}
        onFocus={onFocus}
        onChangeText={onChange}
        placeholder={placeholder}
        placeholderTextColor={colors.composerPlaceholder}
        accessibilityLabel={placeholder}
        style={[type.text, styles.input, { color: colors.textPrimary }]}
      />
      {selected ? <Icon name="check" size={18} color={colors.textPrimary} strokeWidth={2} /> : null}
    </View>
  )
}

function Footer({ children }: { children: ReactNode }) {
  return <View style={styles.footer}>{children}</View>
}

function Failure({ visible }: { visible: boolean }) {
  const { t } = useI18n()
  if (!visible) return null
  return (
    <Txt variant="meta" tone="error" accessibilityLiveRegion="polite">
      {t('decision.failed')}
    </Txt>
  )
}

export function ApprovalBody({ request, disabled, actions }: { request: Approval; disabled: boolean; actions: DecisionActions }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const shell = request.approvalType === 'shell'
  const decide = (decision: ApprovalDecision) => actions.decide(request.requestId, decision)
  return (
    <>
      {request.reason ? <Txt tone="secondary">{request.reason}</Txt> : null}
      <View style={styles.block}>
        <Text selectable style={[type.code, styles.code, { color: colors.textPrimary, backgroundColor: colors.bgTertiary }]}>
          {subjectOf(request)}
        </Text>
        {shell && request.target ? (
          <Txt variant="meta" tone="secondary">
            {t('approval.folder', { folder: baseName(request.target) })}
          </Txt>
        ) : null}
      </View>
      <ChoiceList>
        <ChoiceRow first label={t('approval.allowOnce')} description={t('approval.allowOnce.description')} disabled={disabled} onPress={() => decide('once')} />
        <ChoiceRow
          first={false}
          label={t('approval.allowSession')}
          description={t(shell ? 'approval.allowSession.shellDescription' : 'approval.allowSession.description')}
          disabled={disabled}
          onPress={() => decide('session')}
        />
        <ChoiceRow first={false} label={t('approval.reject')} description={t('approval.reject.description')} disabled={disabled} onPress={() => decide('reject')} />
      </ChoiceList>
    </>
  )
}

function PageButton({ icon, label, disabled, onPress }: { icon: 'chevronLeft' | 'chevronRight'; label: string; disabled: boolean; onPress: () => void }) {
  const { colors } = useTheme()
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
      hitSlop={6}
      style={({ pressed }) => [styles.page, pressed && { backgroundColor: colors.roundFillPressed }, disabled && styles.disabled]}
    >
      <Icon name={icon} size={18} color={colors.textSecondary} />
    </Pressable>
  )
}

export function QuestionBody({ request, disabled, actions }: { request: Question; disabled: boolean; actions: DecisionActions }) {
  const { t } = useI18n()
  const [page, setPage] = useState(0)
  const [choices, setChoices] = useState<QuestionChoice[]>(() => initialChoices(request.questions))
  const [busy, setBusy] = useState(false)
  const [failed, setFailed] = useState(false)
  const questions = request.questions
  const question = questions[page]
  if (!question) return null
  const choice = choices[page]
  const last = page === questions.length - 1
  const otherIndex = question.options.length

  const choose = (patch: Partial<QuestionChoice>) => setChoices((current) => current.map((entry, index) => (index === page ? { ...entry, ...patch } : entry)))

  const submit = () => {
    if (!last) {
      setPage(page + 1)
      return
    }
    actions.answer(request.requestId, questionAnswers(questions, choices, t('question.other')))
  }

  const dismiss = async () => {
    setBusy(true)
    setFailed(false)
    try {
      await actions.dismissQuestion(request)
    } catch {
      setFailed(true)
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      {questions.length > 1 ? (
        <View style={styles.pager}>
          <PageButton icon="chevronLeft" label={t('question.previous')} disabled={page === 0} onPress={() => setPage(page - 1)} />
          <Txt variant="meta" tone="secondary">
            {t('question.step', { index: page + 1, count: questions.length })}
          </Txt>
          <PageButton icon="chevronRight" label={t('question.next')} disabled={last} onPress={() => setPage(page + 1)} />
        </View>
      ) : null}
      <Txt style={styles.strong}>{question.question || question.header}</Txt>
      <ChoiceList>
        {question.options.map((option, index) => (
          <ChoiceRow
            key={`${question.id}:${index}`}
            first={index === 0}
            label={option.label}
            description={option.description}
            selected={choice.option === index}
            disabled={disabled}
            onPress={() => choose({ option: index })}
          />
        ))}
        {hasOther(question) ? (
          <InputRow
            key={question.id}
            value={choice.other}
            placeholder={t('question.otherPlaceholder')}
            selected={choice.option === otherIndex}
            secret={question.isSecret === true}
            disabled={disabled}
            onFocus={() => choose({ option: otherIndex })}
            onChange={(other) => choose({ option: otherIndex, other })}
          />
        ) : null}
      </ChoiceList>
      <Failure visible={failed} />
      <Footer>
        <PhoneButton compact variant="ghost" loading={busy} disabled={disabled} onPress={() => void dismiss()}>
          {t('question.dismiss')}
        </PhoneButton>
        <PhoneButton compact disabled={disabled || busy} onPress={submit}>
          {last ? t('question.submit') : t('question.continue')}
        </PhoneButton>
      </Footer>
    </>
  )
}

export function PlanBody({ disabled, actions }: { disabled: boolean; actions: DecisionActions }) {
  const { t } = useI18n()
  const [text, setText] = useState('')
  const [adjusting, setAdjusting] = useState(false)
  const [busy, setBusy] = useState(false)
  const [failed, setFailed] = useState(false)

  const run = async (action: () => Promise<void>) => {
    setBusy(true)
    setFailed(false)
    try {
      await action()
    } catch {
      setFailed(true)
      setBusy(false)
    }
  }

  const submit = () => {
    const feedback = text.trim()
    void run(feedback ? () => actions.feedback(feedback) : actions.implement)
  }

  return (
    <>
      <ChoiceList>
        <ChoiceRow first label={t('plan.yes')} disabled={disabled || busy} onPress={() => void run(actions.implement)} />
        <InputRow
          value={text}
          placeholder={t('plan.adjust')}
          selected={adjusting}
          secret={false}
          disabled={disabled || busy}
          onFocus={() => setAdjusting(true)}
          onChange={(value) => {
            setText(value)
            setAdjusting(true)
          }}
        />
      </ChoiceList>
      <Failure visible={failed} />
      <Footer>
        <PhoneButton compact variant="ghost" disabled={busy} onPress={actions.dismissPlan}>
          {t('plan.dismiss')}
        </PhoneButton>
        <PhoneButton compact loading={busy} disabled={disabled} onPress={submit}>
          {t('plan.submit')}
        </PhoneButton>
      </Footer>
    </>
  )
}

const styles = StyleSheet.create({
  strong: { fontWeight: '600' },
  disabled: { opacity: 0.45 },
  block: { gap: 6 },
  code: { padding: 12, borderRadius: 12, lineHeight: 20, overflow: 'hidden' },
  choices: { borderWidth: 1, borderRadius: 12, overflow: 'hidden' },
  choice: { flexDirection: 'row', alignItems: 'center', gap: 10, minHeight: 52, paddingVertical: 10, paddingHorizontal: 14 },
  choiceText: { flex: 1, minWidth: 0, gap: 2 },
  input: { flex: 1, minWidth: 0, maxHeight: 120, padding: 0, outlineWidth: 0 },
  pager: { flexDirection: 'row', alignItems: 'center', gap: 6, marginBottom: -4 },
  page: { width: 28, height: 28, borderRadius: 14, alignItems: 'center', justifyContent: 'center' },
  footer: { flexDirection: 'row', justifyContent: 'flex-end', gap: 8 },
})
