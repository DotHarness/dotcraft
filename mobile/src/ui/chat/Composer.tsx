import { useEffect, useEffectEvent, useRef, useState, type ReactNode } from 'react'
import { Keyboard, Platform, Pressable, StyleSheet, Text, TextInput, View } from 'react-native'
import { sendDraft, type SendFailure } from '../../core/attachments'
import {
  chooseEntry,
  draftPieces,
  editText,
  EMPTY_DRAFT,
  isEmptyDraft,
  matchingEntries,
  referencePicker,
  referenceToken,
  type DraftEdit,
  type MessageDraft,
  type ReferenceEntry,
} from '../../core/draft'
import { useI18n } from '../../i18n'
import { pickFile, pickPhotos } from '../../platform/attachmentPicker'
import { Icon, StopGlyph } from '../icons'
import { Spinner, Txt } from '../parts'
import { type, useTheme } from '../theme'
import { AddMenu } from './AddMenu'
import { ComposerAttachments } from './ComposerAttachments'
import { ReferencePicker } from './ReferencePicker'

export interface PendingSend {
  draft: MessageDraft
  sent: Promise<void>
}

function SendButton({ label, disabled, busy, onPress }: { label: string; disabled: boolean; busy: boolean; onPress: () => void }) {
  const { colors } = useTheme()
  const filled = busy || !disabled
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      accessibilityState={{ disabled: disabled || busy, busy }}
      disabled={disabled || busy}
      onPress={onPress}
      style={[styles.send, { backgroundColor: filled ? colors.textPrimary : colors.sendDisabled }]}
    >
      {busy ? <Spinner size={18} color={colors.bgPrimary} /> : <Icon name="arrowUp" size={18} color={filled ? colors.bgPrimary : colors.textDimmed} strokeWidth={2} />}
    </Pressable>
  )
}

export function Composer({
  running,
  controls,
  autoFocus = false,
  initialDraft = EMPTY_DRAFT,
  clearOnSend = true,
  pendingSend,
  onPendingSettled,
  onDraftChange,
  canSend,
  canAttachFiles,
  canPlan,
  references,
  planMode,
  onPlanMode,
  onSend,
  onStop,
}: {
  running: boolean
  controls?: ReactNode
  autoFocus?: boolean
  initialDraft?: MessageDraft
  clearOnSend?: boolean
  pendingSend?: PendingSend
  onPendingSettled?: () => void
  onDraftChange?: (draft: MessageDraft) => void
  canSend: boolean
  canAttachFiles: boolean
  canPlan: boolean
  references: ReferenceEntry[]
  planMode: boolean
  onPlanMode: (on: boolean) => Promise<void>
  onSend: (draft: MessageDraft) => Promise<void>
  onStop: () => void
}) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const [draft, setDraft] = useState<MessageDraft>(initialDraft)
  const [cursor, setCursor] = useState<number | null>(null)
  const [selection, setSelection] = useState<{ start: number; end: number } | undefined>(undefined)
  const [focused, setFocused] = useState(false)
  const [sending, setSending] = useState(pendingSend !== undefined)
  const [notice, setNotice] = useState<string | null>(null)
  const [menuOpen, setMenuOpen] = useState(false)
  const input = useRef<TextInput>(null)
  const empty = isEmptyDraft(draft)
  const match = referencePicker(draft, cursor ?? draft.text.length)
  const matches = match ? matchingEntries(references, match) : []

  useEffect(() => {
    onDraftChange?.(draft)
  }, [draft, onDraftChange])

  useEffect(() => {
    const hidden = Keyboard.addListener('keyboardDidHide', () => input.current?.blur())
    return () => hidden.remove()
  }, [])

  const update = (next: (current: MessageDraft) => MessageDraft) => {
    setDraft(next)
    setNotice(null)
  }

  const place = ({ draft: next, cursor: at }: DraftEdit) => {
    update(() => next)
    setCursor(at)
    setSelection({ start: at, end: at })
  }

  const settle = (submitted: MessageDraft, failure: SendFailure | null) => {
    if (failure) {
      setDraft(submitted)
      setNotice(failure.kind === 'upload' ? t('composer.uploadFailed', { file: failure.file }) : t('composer.failed'))
    } else if (!clearOnSend) {
      setDraft(EMPTY_DRAFT)
    }
    setSending(false)
  }

  const settlePending = useEffectEvent((submitted: MessageDraft, failure: SendFailure | null) => {
    settle(submitted, failure)
    onPendingSettled?.()
  })

  useEffect(() => {
    if (!pendingSend) return
    let current = true
    void sendDraft(pendingSend.draft, () => pendingSend.sent).then((failure) => {
      if (current) settlePending(pendingSend.draft, failure)
    })
    return () => {
      current = false
    }
  }, [pendingSend])

  async function submit() {
    const submitted = draft
    setSending(true)
    setNotice(null)
    if (clearOnSend) {
      setDraft(EMPTY_DRAFT)
      setCursor(null)
    }
    settle(submitted, await sendDraft(submitted, onSend))
  }

  async function attach(pick: () => Promise<void>) {
    setMenuOpen(false)
    try {
      await pick()
    } catch {
      setNotice(t('composer.attachFailed'))
    }
  }

  const addPhotos = () =>
    attach(async () => {
      const picked = await pickPhotos()
      if (picked.length > 0) update((current) => ({ ...current, photos: [...current.photos, ...picked] }))
    })

  const addFile = () =>
    attach(async () => {
      const picked = await pickFile()
      if (picked?.kind === 'tooLarge') setNotice(t('composer.fileTooLarge', { file: picked.name }))
      else if (picked) update((current) => ({ ...current, files: [...current.files, picked.file] }))
    })

  const switchPlan = async (on: boolean) => {
    setMenuOpen(false)
    try {
      await onPlanMode(on)
    } catch {
      setNotice(t('controls.failed'))
    }
  }

  const choose = (entry: ReferenceEntry) => {
    if (!match) return
    place(chooseEntry(draft, match, entry))
    input.current?.focus()
  }

  return (
    <View style={styles.wrap}>
      {notice ? (
        <Txt variant="meta" tone="error" accessibilityLiveRegion="polite">
          {notice}
        </Txt>
      ) : null}
      {matches.length > 0 ? <ReferencePicker entries={matches} onChoose={choose} /> : null}
      <View
        style={[
          styles.card,
          {
            borderColor: focused ? colors.composerFocusBorder : colors.composerInputBorder,
            backgroundColor: colors.composerInputBackground,
            boxShadow: colors.shadow1,
          },
        ]}
      >
        <ComposerAttachments
          photos={draft.photos}
          files={draft.files}
          onRemovePhoto={(id) => update((current) => ({ ...current, photos: current.photos.filter((photo) => photo.id !== id) }))}
          onRemoveFile={(id) => update((current) => ({ ...current, files: current.files.filter((file) => file.id !== id) }))}
        />
        <TextInput
          ref={input}
          multiline
          numberOfLines={Platform.OS === 'web' ? 2 : undefined}
          autoFocus={autoFocus}
          selection={selection}
          onChangeText={(value) => {
            const edited = editText(draft, value)
            if (edited.draft.text !== value) return place(edited)
            update(() => edited.draft)
            setCursor(null)
          }}
          onSelectionChange={({ nativeEvent }) => {
            setCursor(nativeEvent.selection.end)
            setSelection(undefined)
          }}
          onFocus={() => setFocused(true)}
          onBlur={() => setFocused(false)}
          placeholder={t(planMode && canPlan ? 'composer.placeholderPlan' : 'composer.placeholder')}
          placeholderTextColor={colors.composerPlaceholder}
          accessibilityLabel={t('composer.message')}
          style={[type.text, styles.input, { color: colors.textPrimary }]}
        >
          {draftPieces(draft).map((piece, index) =>
            piece.type === 'text' ? (
              piece.value
            ) : (
              <Text key={index} style={[styles.reference, { backgroundColor: colors.roundFill }]}>
                {referenceToken(piece.reference)}
              </Text>
            ),
          )}
        </TextInput>
        <View style={styles.bar}>
          <View style={styles.controls}>
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={t('composer.add')}
              onPress={() => {
                Keyboard.dismiss()
                setMenuOpen(true)
              }}
              hitSlop={4}
              style={({ pressed }) => [styles.round, pressed && { backgroundColor: colors.roundFill }]}
            >
              <Icon name="plus" size={22} color={colors.textPrimary} />
            </Pressable>
            {planMode && canPlan ? (
              <Pressable
                accessibilityRole="button"
                accessibilityLabel={t('composer.planOff')}
                onPress={() => void switchPlan(false)}
                style={({ pressed }) => [styles.plan, { backgroundColor: pressed ? colors.roundFillPressed : colors.roundFill }]}
              >
                <Icon name="listChecks" size={16} color={colors.textPrimary} />
                <Text style={[type.body, styles.planLabel, { color: colors.textPrimary }]}>{t('composer.plan')}</Text>
                <Icon name="x" size={14} color={colors.textSecondary} strokeWidth={2} />
              </Pressable>
            ) : null}
            {controls}
          </View>
          {running && empty ? (
            <Pressable
              accessibilityRole="button"
              accessibilityLabel={t('composer.stop')}
              disabled={!canSend}
              onPress={onStop}
              style={[styles.stop, { backgroundColor: canSend ? colors.textPrimary : colors.sendDisabled }]}
            >
              <StopGlyph size={13} color={canSend ? colors.bgPrimary : colors.textDimmed} />
            </Pressable>
          ) : (
            <SendButton label={t('composer.send')} disabled={empty || !canSend} busy={sending} onPress={() => void submit()} />
          )}
        </View>
      </View>
      <AddMenu
        visible={menuOpen}
        canAttachFiles={canAttachFiles}
        canPlan={canPlan}
        planMode={planMode}
        onClose={() => setMenuOpen(false)}
        onPhoto={() => void addPhotos()}
        onFile={() => void addFile()}
        onPlanMode={(on) => void switchPlan(on)}
      />
    </View>
  )
}

const styles = StyleSheet.create({
  wrap: { gap: 6 },
  card: { borderWidth: 1, borderRadius: 26, paddingTop: 6, paddingBottom: 8, paddingHorizontal: 8 },
  reference: { fontWeight: '500' },
  input: { minHeight: 40, maxHeight: 140, paddingTop: 8, paddingBottom: 6, paddingHorizontal: 10, outlineWidth: 0 },
  bar: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  controls: { flex: 1, minWidth: 0, flexDirection: 'row', alignItems: 'center', gap: 4 },
  round: { width: 36, height: 36, borderRadius: 18, alignItems: 'center', justifyContent: 'center' },
  plan: { flexDirection: 'row', alignItems: 'center', gap: 5, height: 32, paddingLeft: 9, paddingRight: 8, borderRadius: 16 },
  planLabel: { fontWeight: '500' },
  stop: { width: 36, height: 36, borderRadius: 18, alignItems: 'center', justifyContent: 'center' },
  send: { width: 36, height: 36, borderRadius: 18, alignItems: 'center', justifyContent: 'center' },
})
