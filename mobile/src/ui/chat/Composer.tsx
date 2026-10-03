import { useState, type ReactNode } from 'react'
import { Platform, Pressable, StyleSheet, TextInput, View } from 'react-native'
import { useI18n } from '../../i18n'
import { StopGlyph } from '../icons'
import { Txt } from '../parts'
import { type, useTheme } from '../theme'
import { SendButton } from './RequestCards'

export function Composer({
  running,
  mascot,
  autoFocus = false,
  canSend,
  onSend,
  onStop,
}: {
  running: boolean
  mascot: ReactNode
  autoFocus?: boolean
  canSend: boolean
  onSend: (text: string) => Promise<void>
  onStop: () => void
}) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const [draft, setDraft] = useState('')
  const [focused, setFocused] = useState(false)
  const [sending, setSending] = useState(false)
  const [failed, setFailed] = useState(false)
  const empty = draft.trim().length === 0

  async function submit() {
    const text = draft.trim()
    setSending(true)
    setFailed(false)
    setDraft('')
    try {
      await onSend(text)
    } catch {
      setDraft(text)
      setFailed(true)
    } finally {
      setSending(false)
    }
  }

  return (
    <View style={styles.wrap}>
      {failed ? (
        <Txt variant="meta" tone="error" accessibilityLiveRegion="polite">
          {t('composer.failed')}
        </Txt>
      ) : null}
      <View style={styles.row}>
        <View style={styles.mascot}>{mascot}</View>
        <View
          style={[
            styles.composer,
            {
              borderColor: focused ? colors.composerFocusBorder : colors.composerInputBorder,
              backgroundColor: colors.composerInputBackground,
            },
          ]}
        >
          <TextInput
            multiline
            numberOfLines={Platform.OS === 'web' ? 1 : undefined}
            autoFocus={autoFocus}
            value={draft}
            onChangeText={(value) => {
              setDraft(value)
              setFailed(false)
            }}
            onFocus={() => setFocused(true)}
            onBlur={() => setFocused(false)}
            placeholder={running ? t('composer.addToTurn') : t('composer.placeholder')}
            placeholderTextColor={colors.composerPlaceholder}
            accessibilityLabel={t('composer.message')}
            style={[type.text, styles.input, { color: colors.textPrimary }]}
          />
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
            <SendButton label={t('composer.send')} disabled={empty || !canSend || sending} onPress={() => void submit()} />
          )}
        </View>
      </View>
    </View>
  )
}

const styles = StyleSheet.create({
  wrap: { gap: 6 },
  row: { flexDirection: 'row', alignItems: 'flex-end', gap: 6 },
  mascot: { marginBottom: 6 },
  composer: {
    flex: 1,
    minWidth: 0,
    flexDirection: 'row',
    alignItems: 'flex-end',
    gap: 8,
    minHeight: 48,
    paddingVertical: 6,
    paddingRight: 6,
    paddingLeft: 16,
    borderWidth: 1,
    borderRadius: 24,
  },
  input: { flex: 1, minWidth: 0, maxHeight: 120, paddingVertical: 8, paddingHorizontal: 0, outlineWidth: 0 },
  stop: { width: 36, height: 36, borderRadius: 18, alignItems: 'center', justifyContent: 'center' },
})
