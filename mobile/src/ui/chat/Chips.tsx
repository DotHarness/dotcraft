import * as Clipboard from 'expo-clipboard'
import { createContext, useContext, useState, type ReactNode } from 'react'
import { Linking, Platform, Pressable, StyleSheet, Text, View } from 'react-native'
import { useMobileState } from '../../app-state/SessionContext'
import { baseName } from '../../core/transcript'
import { useI18n } from '../../i18n'
import { Icon, type IconName } from '../icons'
import { PhoneButton, Txt } from '../parts'
import { SheetHeader, SheetLayer } from '../Sheet'
import { type, useTheme } from '../theme'

function Chip({ icon, label, onPress, accessibilityLabel }: { icon: IconName; label: string; onPress?: () => void; accessibilityLabel?: string }) {
  const { colors } = useTheme()
  return (
    <Pressable
      accessibilityRole={onPress ? 'button' : 'text'}
      accessibilityLabel={accessibilityLabel ?? label}
      disabled={!onPress}
      onPress={onPress}
      hitSlop={4}
      style={({ pressed }) => [styles.chip, { backgroundColor: pressed ? colors.roundFillPressed : colors.roundFill }]}
    >
      <Icon name={icon} size={12} color={colors.textSecondary} strokeWidth={2} />
      <Text numberOfLines={1} style={[type.meta, styles.label, { color: colors.textPrimary }]}>
        {label}
      </Text>
    </Pressable>
  )
}

export function FilePath({ path, note }: { path: string; note?: string }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const computer = useMobileState().computer?.name ?? ''
  const [copied, setCopied] = useState(false)
  return (
    <>
      <Txt tone="secondary">{note ?? t('file.onComputer', { computer })}</Txt>
      <Text selectable style={[type.code, styles.path, { color: colors.textPrimary, backgroundColor: colors.bgTertiary }]}>
        {path}
      </Text>
      <PhoneButton
        variant="outline"
        icon={copied ? 'check' : 'copy'}
        onPress={() => {
          void Clipboard.setStringAsync(path).then(() => setCopied(true))
        }}
      >
        {copied ? t('common.copied') : t('file.copyPath')}
      </PhoneButton>
    </>
  )
}

function FilePathSheet({ path, visible, onClose }: { path: string; visible: boolean; onClose: () => void }) {
  return (
    <SheetLayer visible={visible} onClose={onClose}>
      <SheetHeader title={baseName(path)} />
      <FilePath path={path} />
    </SheetLayer>
  )
}

export const FileViewerContext = createContext<((path: string) => void) | null>(null)

export function FileChip({ path, label }: { path: string; label: string }) {
  const { t } = useI18n()
  const openFile = useContext(FileViewerContext)
  const [open, setOpen] = useState(false)
  if (openFile) return <Chip icon="file" label={label} accessibilityLabel={t('file.open', { file: label })} onPress={() => openFile(path)} />
  return (
    <>
      <Chip icon="file" label={label} accessibilityLabel={t('file.show', { file: label })} onPress={() => setOpen(true)} />
      <FilePathSheet path={path} visible={open} onClose={() => setOpen(false)} />
    </>
  )
}

export function LinkChip({ url, label }: { url: string; label: string }) {
  return <Chip icon={/^https?:/i.test(url) ? 'globe' : 'link'} label={label} onPress={() => void Linking.openURL(url).catch(() => undefined)} />
}

export function SkillChip({ name }: { name: string }) {
  return <Chip icon="box" label={name} />
}

export function InlineChip({ children }: { children: ReactNode }) {
  return <View style={styles.inline}>{children}</View>
}

const styles = StyleSheet.create({
  chip: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    maxWidth: 240,
    height: 22,
    paddingHorizontal: 6,
    borderRadius: 6,
  },
  label: { flexShrink: 1, fontWeight: '500' },
  inline: { paddingHorizontal: 2, transform: [{ translateY: Platform.OS === 'web' ? 0 : 4 }] },
  path: { padding: 12, borderRadius: 12, lineHeight: 20, overflow: 'hidden' },
})
