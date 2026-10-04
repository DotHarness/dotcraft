import { useRef, useState, type ReactNode } from 'react'
import { Pressable, StyleSheet, Text, TextInput, View } from 'react-native'
import { hasOther, initialChoices, questionAnswers, type QuestionChoice } from '../../core/decisions'
import type { ApprovalDecision } from '../../core/projectConnection'
import type { PendingRequest } from '../../core/state'
import { useI18n } from '../../i18n'
import type { MessageId } from '../../i18n/messages/en'
import { Icon } from '../icons'
import { PhoneButton, Txt } from '../parts'
import { type, useTheme } from '../theme'
import { approvalTitle } from './approvalText'

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

function Title({ children }: { children: string }) {
  return (
    <Txt accessibilityRole="header" style={styles.title}>
      {children}
    </Txt>
  )
}

function ChoiceRow({
  index,
  label,
  description,
  selected,
  disabled,
  onPress,
}: {
  index: number
  label: string
  description?: string
  selected: boolean
  disabled: boolean
  onPress: () => void
}) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const [explained, setExplained] = useState(false)
  const about = description?.trim() ?? ''
  return (
    <Pressable
      accessibilityRole="radio"
      accessibilityState={{ checked: selected, disabled }}
      accessibilityLabel={`${index + 1}. ${label}`}
      accessibilityHint={about || undefined}
      disabled={disabled}
      onPress={onPress}
      style={({ pressed }) => [
        styles.choice,
        { backgroundColor: selected ? colors.controlActive : pressed ? colors.controlHover : 'transparent' },
        disabled && styles.disabled,
      ]}
    >
      <View style={styles.choiceLine}>
        <Text style={[type.meta, styles.number, { color: colors.textDimmed }]}>{`${index + 1}.`}</Text>
        <Txt style={styles.label}>{label}</Txt>
        {about ? (
          <Pressable
            accessibilityRole="button"
            accessibilityLabel={t('decision.about', { option: label })}
            accessibilityState={{ expanded: explained }}
            hitSlop={8}
            onPress={() => setExplained((value) => !value)}
          >
            <Icon name="circleAlert" size={14} color={colors.textDimmed} />
          </Pressable>
        ) : null}
      </View>
      {explained ? (
        <Txt variant="meta" tone="secondary" style={styles.about}>
          {about}
        </Txt>
      ) : null}
    </Pressable>
  )
}

function InputRow({
  index,
  value,
  placeholder,
  selected,
  secret,
  disabled,
  onFocus,
  onBlur,
  onChange,
}: {
  index: number
  value: string
  placeholder: string
  selected: boolean
  secret: boolean
  disabled: boolean
  onFocus: () => void
  onBlur?: () => void
  onChange: (value: string) => void
}) {
  const { colors } = useTheme()
  const input = useRef<TextInput>(null)
  return (
    <Pressable
      accessible={false}
      disabled={disabled}
      onPress={() => input.current?.focus()}
      style={[styles.choice, { backgroundColor: selected ? colors.controlActive : 'transparent' }, disabled && styles.disabled]}
    >
      <View style={[styles.choiceLine, styles.inputLine]}>
        <Text style={[type.meta, styles.number, { color: colors.textDimmed }]}>{`${index + 1}.`}</Text>
        <TextInput
          ref={input}
          value={value}
          editable={!disabled}
          multiline={!secret}
          secureTextEntry={secret}
          onFocus={onFocus}
          onBlur={onBlur}
          onChangeText={onChange}
          placeholder={placeholder}
          placeholderTextColor={colors.composerPlaceholder}
          accessibilityLabel={`${index + 1}. ${placeholder}`}
          style={[type.text, styles.input, { color: colors.textPrimary }]}
        />
      </View>
    </Pressable>
  )
}

function Choices({ children }: { children: ReactNode }) {
  return <View style={styles.choices}>{children}</View>
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

function typeLabel(approvalType: string): MessageId {
  if (approvalType === 'file') return 'approval.type.file'
  if (approvalType === 'remoteResource') return 'approval.type.remoteResource'
  if (approvalType === 'skill') return 'approval.type.skill'
  if (approvalType === 'computerUse') return 'approval.type.computerUse'
  return 'approval.type.shell'
}

function DetailRow({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  const { colors } = useTheme()
  return (
    <View style={styles.detailRow}>
      <Txt tone="dimmed" style={styles.detailLabel}>
        {label}
      </Txt>
      <Text selectable style={[mono ? type.code : type.text, styles.detailValue, { color: colors.textSecondary }]}>
        {value}
      </Text>
    </View>
  )
}

export function ApprovalBody({ request, disabled, actions }: { request: Approval; disabled: boolean; actions: DecisionActions }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const [selected, setSelected] = useState(0)
  const shell = request.approvalType === 'shell'
  const options: { decision: ApprovalDecision; label: string; description: string }[] = [
    { decision: 'once', label: t('approval.allowOnce'), description: t('approval.allowOnce.description') },
    {
      decision: 'session',
      label: t('approval.allowSession'),
      description: t(shell ? 'approval.allowSession.shellDescription' : 'approval.allowSession.description'),
    },
    { decision: 'reject', label: t('approval.reject'), description: t('approval.reject.description') },
  ]
  const rejectIndex = options.length - 1
  const decide = (index: number) => actions.decide(request.requestId, options[index].decision)
  const computerUse = request.approvalType === 'computerUse'
  const operation = request.operation.trim()
  const target = request.target.trim()
  const reason = request.reason.trim()

  return (
    <>
      <Title>{approvalTitle(request, t)}</Title>
      <View style={[styles.details, { borderColor: colors.borderDefault, backgroundColor: colors.bgPrimary }]}>
        <DetailRow label={t('approval.detail.type')} value={t(typeLabel(request.approvalType))} />
        {!computerUse && operation ? <DetailRow label={t('approval.detail.operation')} value={operation} mono /> : null}
        {target ? <DetailRow label={t('approval.detail.target')} value={target} mono /> : null}
        {!computerUse && reason ? <DetailRow label={t('approval.detail.reason')} value={reason} /> : null}
      </View>
      <Choices>
        {options.map((option, index) => (
          <ChoiceRow
            key={option.decision}
            index={index}
            label={option.label}
            description={option.description}
            selected={selected === index}
            disabled={disabled}
            onPress={() => (selected === index ? decide(index) : setSelected(index))}
          />
        ))}
      </Choices>
      <Footer>
        {selected !== rejectIndex ? (
          <PhoneButton compact variant="ghost" disabled={disabled} onPress={() => decide(rejectIndex)}>
            {options[rejectIndex].label}
          </PhoneButton>
        ) : null}
        <PhoneButton compact disabled={disabled} onPress={() => decide(selected)}>
          {options[selected].label}
        </PhoneButton>
      </Footer>
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
      <Icon name={icon} size={16} color={colors.textSecondary} />
    </Pressable>
  )
}

export function QuestionBody({
  request,
  disabled,
  actions,
  onEditing,
}: {
  request: Question
  disabled: boolean
  actions: DecisionActions
  onEditing: (editing: boolean) => void
}) {
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
      <View style={styles.titleRow}>
        <View style={styles.titleText}>
          <Title>{question.question || question.header}</Title>
        </View>
        {questions.length > 1 ? (
          <View style={styles.pager}>
            <PageButton icon="chevronLeft" label={t('question.previous')} disabled={page === 0} onPress={() => setPage(page - 1)} />
            <Txt variant="meta" tone="secondary">
              {t('decision.count', { index: page + 1, count: questions.length })}
            </Txt>
            <PageButton icon="chevronRight" label={t('question.next')} disabled={last} onPress={() => setPage(page + 1)} />
          </View>
        ) : null}
      </View>
      <Choices>
        {question.options.map((option, index) => (
          <ChoiceRow
            key={`${question.id}:${index}`}
            index={index}
            label={option.label}
            description={option.description}
            selected={choice.option === index}
            disabled={disabled}
            onPress={() => (choice.option === index ? submit() : choose({ option: index }))}
          />
        ))}
        {hasOther(question) ? (
          <InputRow
            key={question.id}
            index={otherIndex}
            value={choice.other}
            placeholder={t('question.otherPlaceholder')}
            selected={choice.option === otherIndex}
            secret={question.isSecret === true}
            disabled={disabled}
            onFocus={() => {
              choose({ option: otherIndex })
              onEditing(true)
            }}
            onBlur={() => onEditing(false)}
            onChange={(other) => choose({ option: otherIndex, other })}
          />
        ) : null}
      </Choices>
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

export function PlanBody({ disabled, actions, onEditing }: { disabled: boolean; actions: DecisionActions; onEditing: (editing: boolean) => void }) {
  const { t } = useI18n()
  const [text, setText] = useState('')
  const [selected, setSelected] = useState(0)
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
      <Title>{t('plan.title')}</Title>
      <Choices>
        <ChoiceRow index={0} label={t('plan.yes')} selected={selected === 0} disabled={disabled || busy} onPress={() => void run(actions.implement)} />
        <InputRow
          index={1}
          value={text}
          placeholder={t('plan.adjust')}
          selected={selected === 1}
          secret={false}
          disabled={disabled || busy}
          onFocus={() => {
            setSelected(1)
            onEditing(true)
          }}
          onBlur={() => onEditing(false)}
          onChange={setText}
        />
      </Choices>
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
  disabled: { opacity: 0.72 },
  title: { fontWeight: '600' },
  titleRow: { flexDirection: 'row', alignItems: 'flex-start', gap: 12 },
  titleText: { flex: 1, minWidth: 0 },
  details: { gap: 4, paddingVertical: 8, paddingHorizontal: 10, borderWidth: 1, borderRadius: 8 },
  detailRow: { flexDirection: 'row', alignItems: 'flex-start', gap: 8 },
  detailLabel: { width: 72 },
  detailValue: { flex: 1, minWidth: 0, lineHeight: 20 },
  choices: { gap: 4 },
  choice: { minHeight: 40, justifyContent: 'center', paddingVertical: 8, paddingHorizontal: 12, borderRadius: 8 },
  choiceLine: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  inputLine: { alignItems: 'flex-start' },
  number: { width: 20, lineHeight: 20 },
  label: { flexShrink: 1 },
  about: { marginTop: 2, marginLeft: 28 },
  input: { flex: 1, minWidth: 0, maxHeight: 120, padding: 0, lineHeight: 20, outlineWidth: 0 },
  pager: { flexDirection: 'row', alignItems: 'center', gap: 2 },
  page: { width: 26, height: 26, borderRadius: 13, alignItems: 'center', justifyContent: 'center' },
  footer: { flexDirection: 'row', justifyContent: 'flex-end', gap: 8 },
})
