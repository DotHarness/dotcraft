import { useState, type ReactNode } from 'react'
import { Pressable, StyleSheet, Text, TextInput, View } from 'react-native'
import type { ApprovalDecision } from '../../core/projectConnection'
import type { PendingRequest } from '../../core/state'
import { baseName } from '../../core/transcript'
import { useI18n, type I18n } from '../../i18n'
import { Icon } from '../icons'
import { PhoneButton, Txt } from '../parts'
import { SheetHeader, SheetLayer } from '../Sheet'
import { metrics, type, useTheme } from '../theme'

type Approval = Extract<PendingRequest, { kind: 'approval' }>
type Question = Extract<PendingRequest, { kind: 'question' }>

export function approvalTitle(request: Approval, t: I18n['t']): string {
  if (request.approvalType === 'shell') return t('approval.runCommand')
  if (request.approvalType === 'file') {
    if (request.operation === 'read' || request.operation === 'list') return t('approval.readFile')
    return t('approval.changeFile')
  }
  return t('approval.other')
}

export function subjectOf(request: Approval): string {
  if (request.approvalType === 'shell') return request.operation
  if (request.approvalType === 'file') return request.target
  return [request.operation, request.targetLabel ?? request.target].filter(Boolean).join(' · ')
}

function ApprovalActions({ disabled, onDecide, height }: { disabled: boolean; onDecide: (decision: ApprovalDecision) => void; height?: number }) {
  const { t } = useI18n()
  return (
    <View style={styles.decision}>
      <PhoneButton height={height} disabled={disabled} onPress={() => onDecide('once')}>
        {t('approval.allowOnce')}
      </PhoneButton>
      <PhoneButton height={height} variant="outline" disabled={disabled} onPress={() => onDecide('session')}>
        {t('approval.allowSession')}
      </PhoneButton>
      <PhoneButton height={height} variant="outline" disabled={disabled} onPress={() => onDecide('reject')}>
        {t('approval.reject')}
      </PhoneButton>
    </View>
  )
}

function Card({ label, children }: { label: string; children: ReactNode }) {
  const { colors } = useTheme()
  return (
    <View
      accessibilityLabel={label}
      style={[styles.card, { borderColor: colors.borderDefault, backgroundColor: colors.bgSecondary, boxShadow: colors.shadow1 }]}
    >
      {children}
    </View>
  )
}

export function ApprovalCard({
  request,
  disabled,
  onDecide,
  onDetails,
}: {
  request: Approval
  disabled: boolean
  onDecide: (decision: ApprovalDecision) => void
  onDetails: () => void
}) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const shell = request.approvalType === 'shell'
  return (
    <Card label={t('approval.label')}>
      <View style={styles.head}>
        <Txt accessibilityRole="header" style={[styles.headTitle, styles.shrink]}>
          {approvalTitle(request, t)}
        </Txt>
        <Pressable accessibilityRole="link" onPress={onDetails} hitSlop={8}>
          {({ pressed }) => (
            <Txt variant="meta" style={[styles.link, { color: pressed ? colors.textPrimary : colors.textSecondary }]}>
              {t('approval.details')}
            </Txt>
          )}
        </Pressable>
      </View>
      {request.reason ? (
        <Txt variant="meta" tone="secondary" style={styles.reason}>
          {request.reason}
        </Txt>
      ) : null}
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={t('approval.showFull')}
        onPress={onDetails}
        style={[styles.subject, { backgroundColor: colors.bgTertiary }]}
      >
        <Text numberOfLines={2} style={[shell ? type.code : type.meta, styles.subjectText, { color: shell ? colors.textPrimary : colors.textSecondary }]}>
          {subjectOf(request)}
        </Text>
      </Pressable>
      <ApprovalActions disabled={disabled} onDecide={onDecide} height={metrics.touch - 4} />
    </Card>
  )
}

export function QuestionCard({
  request,
  disabled,
  onAnswer,
}: {
  request: Question
  disabled: boolean
  onAnswer: (answers: Record<string, string[]>) => void
}) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const [index, setIndex] = useState(0)
  const [answers, setAnswers] = useState<Record<string, string[]>>({})
  const [typing, setTyping] = useState(false)
  const [draft, setDraft] = useState('')
  const question = request.questions[index]
  if (!question) return null
  const last = index === request.questions.length - 1

  const choose = (value: string) => {
    const next = { ...answers, [question.id]: [value] }
    setTyping(false)
    setDraft('')
    if (last) onAnswer(next)
    else {
      setAnswers(next)
      setIndex(index + 1)
    }
  }

  return (
    <Card label={t('question.label')}>
      {request.questions.length > 1 ? (
        <Txt variant="caption" tone="secondary">
          {t('question.step', { index: index + 1, count: request.questions.length })}
        </Txt>
      ) : null}
      <View style={styles.head}>
        <Txt accessibilityRole="header" style={[styles.headTitle, styles.shrink]}>
          {question.question}
        </Txt>
      </View>
      <View style={styles.options}>
        {question.options.map((option, optionIndex) => (
          <Pressable
            key={option.label}
            accessibilityRole="button"
            disabled={disabled}
            onPress={() => choose(option.label)}
            style={({ pressed }) => [
              styles.option,
              { borderTopColor: colors.borderDefault },
              pressed && { backgroundColor: colors.bgTertiary },
              !question.isOther && optionIndex === question.options.length - 1 && styles.optionLast,
              disabled && styles.disabled,
            ]}
          >
            <Txt style={styles.optionLabel}>{option.label}</Txt>
            {option.description ? (
              <Txt variant="meta" tone="secondary">
                {option.description}
              </Txt>
            ) : null}
          </Pressable>
        ))}
        {question.isOther ? (
          typing ? (
            <View style={[styles.option, styles.optionInput, styles.optionLast, { borderTopColor: colors.borderDefault }]}>
              <TextInput
                autoFocus
                value={draft}
                onChangeText={setDraft}
                secureTextEntry={question.isSecret}
                placeholder={t('question.type')}
                placeholderTextColor={colors.composerPlaceholder}
                accessibilityLabel={t('question.type')}
                onSubmitEditing={() => draft.trim() && choose(draft.trim())}
                style={[type.text, styles.input, { color: colors.textPrimary }]}
              />
              <SendButton label={t('question.send')} disabled={!draft.trim()} onPress={() => choose(draft.trim())} />
            </View>
          ) : (
            <Pressable
              accessibilityRole="button"
              disabled={disabled}
              onPress={() => setTyping(true)}
              style={({ pressed }) => [
                styles.option,
                styles.optionLast,
                { borderTopColor: colors.borderDefault },
                pressed && { backgroundColor: colors.bgTertiary },
                disabled && styles.disabled,
              ]}
            >
              <Txt tone="secondary">{t('question.type')}</Txt>
            </Pressable>
          )
        ) : null}
      </View>
    </Card>
  )
}

export function SendButton({ label, disabled, onPress }: { label: string; disabled: boolean; onPress: () => void }) {
  const { colors } = useTheme()
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled }}
      disabled={disabled}
      onPress={onPress}
      style={[styles.send, { backgroundColor: disabled ? colors.sendDisabled : colors.textPrimary }]}
    >
      <Icon name="arrowUp" size={18} color={disabled ? colors.textDimmed : colors.bgPrimary} strokeWidth={2} />
    </Pressable>
  )
}

export function ApprovalSheet({
  request,
  visible,
  disabled,
  onClose,
  onDecide,
}: {
  request: Approval
  visible: boolean
  disabled: boolean
  onClose: () => void
  onDecide: (decision: ApprovalDecision) => void
}) {
  const { t } = useI18n()
  const { colors } = useTheme()
  return (
    <SheetLayer visible={visible} onClose={onClose}>
      <SheetHeader title={approvalTitle(request, t)} onClose={onClose} />
      {request.reason ? <Txt tone="secondary">{request.reason}</Txt> : null}
      {request.approvalType === 'shell' ? (
        <View style={styles.block}>
          <Text selectable style={[type.code, styles.codeBlock, { color: colors.textPrimary, backgroundColor: colors.bgTertiary }]}>
            {request.operation}
          </Text>
          {request.target ? (
            <Txt variant="meta" tone="secondary">
              {t('approval.folder', { folder: baseName(request.target) })}
            </Txt>
          ) : null}
        </View>
      ) : request.approvalType === 'file' ? (
        <View accessibilityLabel={t('approval.files')} style={[styles.files, { borderColor: colors.borderDefault }]}>
          <Text selectable style={[type.code, styles.fileRow, { color: colors.textPrimary }]}>
            {request.target}
          </Text>
        </View>
      ) : (
        <Text selectable style={[type.code, styles.codeBlock, { color: colors.textPrimary, backgroundColor: colors.bgTertiary }]}>
          {subjectOf(request)}
        </Text>
      )}
      <ApprovalActions disabled={disabled} onDecide={onDecide} />
    </SheetLayer>
  )
}

const styles = StyleSheet.create({
  shrink: { flexShrink: 1 },
  disabled: { opacity: 0.45 },
  card: { gap: 8, padding: 14, borderWidth: 1, borderRadius: metrics.heroRadius, overflow: 'hidden' },
  head: { flexDirection: 'row', alignItems: 'baseline', justifyContent: 'space-between', gap: 12 },
  headTitle: { fontWeight: '600' },
  link: { fontWeight: '500' },
  reason: { marginTop: -4 },
  subject: { paddingVertical: 8, paddingHorizontal: 10, borderRadius: 10 },
  subjectText: { lineHeight: 18 },
  decision: { gap: 8, marginTop: 4 },
  options: { marginTop: 2, marginHorizontal: -14, marginBottom: -14 },
  option: { minHeight: 48, justifyContent: 'center', gap: 1, paddingVertical: 9, paddingHorizontal: 14, borderTopWidth: 1 },
  optionLast: { borderBottomLeftRadius: metrics.heroRadius, borderBottomRightRadius: metrics.heroRadius },
  optionLabel: { fontWeight: '500' },
  optionInput: { flexDirection: 'row', alignItems: 'center', gap: 8, paddingVertical: 6, paddingLeft: 14, paddingRight: 8 },
  input: { flex: 1, minWidth: 0, padding: 0, outlineWidth: 0 },
  send: { width: 36, height: 36, borderRadius: 18, alignItems: 'center', justifyContent: 'center' },
  block: { gap: 6 },
  codeBlock: { padding: 12, borderRadius: 12, lineHeight: 20, overflow: 'hidden' },
  files: { borderWidth: 1, borderRadius: 12, overflow: 'hidden' },
  fileRow: { paddingVertical: 11, paddingHorizontal: 12, lineHeight: 18 },
})
