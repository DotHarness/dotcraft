import * as Clipboard from 'expo-clipboard'
import { useEffect, useState } from 'react'
import { Pressable, type StyleProp, StyleSheet, type ViewStyle } from 'react-native'
import { useI18n } from '../../i18n'
import { Icon } from '../icons'
import { useTheme } from '../theme'

export function CopyButton({ text, label, style }: { text: string; label: string; style?: StyleProp<ViewStyle> }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const [copied, setCopied] = useState(false)
  useEffect(() => {
    if (!copied) return
    const timer = setTimeout(() => setCopied(false), 1500)
    return () => clearTimeout(timer)
  }, [copied])
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={copied ? t('common.copied') : label}
      hitSlop={6}
      onPress={() => void Clipboard.setStringAsync(text).then(() => setCopied(true))}
      style={({ pressed }) => [styles.copy, style, pressed && { backgroundColor: colors.roundFill }]}
    >
      <Icon name={copied ? 'check' : 'copy'} size={16} color={colors.textDimmed} />
    </Pressable>
  )
}

const styles = StyleSheet.create({
  copy: { padding: 6, borderRadius: 8 },
})
