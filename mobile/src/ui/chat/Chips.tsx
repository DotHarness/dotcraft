import * as Clipboard from 'expo-clipboard'
import { createContext, useContext, useState } from 'react'
import { Linking, Platform, StyleSheet, Text, View } from 'react-native'
import { useComputerSelector } from '../../app-state/SessionContext'
import { baseName } from '../../core/transcript'
import { useI18n } from '../../i18n'
import { Icon, type IconName } from '../icons'
import { PhoneButton, Txt } from '../parts'
import { SheetHeader, SheetLayer } from '../Sheet'
import { type, useTheme } from '../theme'

type RefTone = 'base' | 'skill'

function Ref({
  icon,
  label,
  tone = 'base',
  onPress,
  accessibilityLabel,
}: {
  icon: IconName
  label: string
  tone?: RefTone
  onPress?: () => void
  accessibilityLabel?: string
}) {
  const { colors } = useTheme()
  const [pressed, setPressed] = useState(false)
  const color = pressed ? colors.textPrimary : tone === 'skill' ? colors.refSkill : colors.refBase
  return (
    <Text
      accessibilityRole={onPress ? 'button' : 'text'}
      accessibilityLabel={accessibilityLabel ?? label}
      onPress={onPress}
      onPressIn={onPress ? () => setPressed(true) : undefined}
      onPressOut={onPress ? () => setPressed(false) : undefined}
      suppressHighlighting
      style={[styles.ref, tone === 'skill' && styles.skill, { color }]}
    >
      <View style={styles.icon}>
        <Icon name={icon} size={13} color={color} strokeWidth={2} />
      </View>
      {label}
    </Text>
  )
}

export function FilePath({ path, note }: { path: string; note?: string }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const computer = useComputerSelector((state) => state.computer.name) ?? ''
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

function FileRef({ path, label, icon, tone }: { path: string; label: string; icon: IconName; tone?: RefTone }) {
  const { t } = useI18n()
  const openFile = useContext(FileViewerContext)
  const [open, setOpen] = useState(false)
  if (openFile) return <Ref icon={icon} label={label} tone={tone} accessibilityLabel={t('file.open', { file: label })} onPress={() => openFile(path)} />
  return (
    <>
      <Ref icon={icon} label={label} tone={tone} accessibilityLabel={t('file.show', { file: label })} onPress={() => setOpen(true)} />
      <FilePathSheet path={path} visible={open} onClose={() => setOpen(false)} />
    </>
  )
}

export function FileChip({ path, label }: { path: string; label: string }) {
  return <FileRef path={path} label={label} icon="file" />
}

export function LinkChip({ url, label }: { url: string; label: string }) {
  return <Ref icon={/^https?:/i.test(url) ? 'globe' : 'link'} label={label} onPress={() => void Linking.openURL(url).catch(() => undefined)} />
}

export function SkillChip({ name, path }: { name: string; path?: string }) {
  return path ? <FileRef path={path} label={name} icon="box" tone="skill" /> : <Ref icon="box" label={name} tone="skill" />
}

const styles = StyleSheet.create({
  ref: { fontWeight: '500' },
  skill: { fontWeight: '600' },
  // An inline view sits on the baseline, so it is lowered to centre on the label.
  icon: { marginRight: 4, transform: [{ translateY: Platform.OS === 'web' ? 1 : 2 }] },
  path: { padding: 12, borderRadius: 12, lineHeight: 20, overflow: 'hidden' },
})
