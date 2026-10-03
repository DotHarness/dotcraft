import { useState, type ReactNode } from 'react'
import { Platform, Pressable, StyleSheet, TextInput, View } from 'react-native'
import { useI18n } from '../../i18n'
import { StopGlyph } from '../icons'
import { Txt } from '../parts'
import { type, useTheme } from '../theme'
import { SendButton } from './RequestCards'

export function Composer({
  computer,
  running,
  controls,
  autoFocus = false,
  canSend,
  onSend,
  onStop,
}: {
  computer: string
  running: boolean
  controls?: ReactNode
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
        <TextInput
          multiline
          numberOfLines={Platform.OS === 'web' ? 2 : undefined}
          autoFocus={autoFocus}
          value={draft}
          onChangeText={(value) => {
            setDraft(value)
            setFailed(false)
          }}
          onFocus={() => setFocused(true)}
          onBlur={() => setFocused(false)}
          placeholder={t('composer.placeholder', { computer })}
          placeholderTextColor={colors.composerPlaceholder}
          accessibilityLabel={t('composer.message')}
          style={[type.text, styles.input, { color: colors.textPrimary }]}
        />
        <View style={styles.bar}>
          <View style={styles.controls}>{controls}</View>
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
  card: { borderWidth: 1, borderRadius: 26, paddingTop: 6, paddingBottom: 8, paddingHorizontal: 8 },
  input: { minHeight: 40, maxHeight: 140, paddingTop: 8, paddingBottom: 6, paddingHorizontal: 10, outlineWidth: 0 },
  bar: { flexDirection: 'row', alignItems: 'center', gap: 6 },
  controls: { flex: 1, minWidth: 0, flexDirection: 'row', alignItems: 'center', gap: 4 },
  stop: { width: 36, height: 36, borderRadius: 18, alignItems: 'center', justifyContent: 'center' },
})
