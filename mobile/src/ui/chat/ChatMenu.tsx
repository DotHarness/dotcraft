import { useState } from 'react'
import { StyleSheet, TextInput, View } from 'react-native'
import { useI18n } from '../../i18n'
import type { IconName } from '../icons'
import { PhoneButton } from '../parts'
import { SheetHeader, SheetLayer } from '../Sheet'
import { metrics, type, useTheme } from '../theme'

function MenuRow({ icon, label, disabled, onPress }: { icon: IconName; label: string; disabled?: boolean; onPress: () => void }) {
  return (
    <PhoneButton variant="ghost" icon={icon} disabled={disabled} onPress={onPress} style={styles.row}>
      {label}
    </PhoneButton>
  )
}

function Separator() {
  const { colors } = useTheme()
  return <View style={[styles.separator, { backgroundColor: colors.borderDefault }]} />
}

function RenameForm({ title, onSave }: { title: string; onSave: (title: string) => void }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const [draft, setDraft] = useState(title)
  const name = draft.trim()
  const save = () => {
    if (name && name !== title) onSave(name)
  }
  return (
    <>
      <TextInput
        autoFocus
        selectTextOnFocus
        value={draft}
        onChangeText={setDraft}
        onSubmitEditing={save}
        returnKeyType="done"
        accessibilityLabel={t('chat.rename')}
        placeholder={t('chat.untitled')}
        placeholderTextColor={colors.composerPlaceholder}
        style={[type.text, styles.input, { color: colors.textPrimary, borderColor: colors.accent, backgroundColor: colors.bgSecondary }]}
      />
      <PhoneButton disabled={!name || name === title} onPress={save}>
        {t('common.save')}
      </PhoneButton>
    </>
  )
}

export function ChatMenu({
  visible,
  title,
  ready,
  canFork,
  stoppable,
  onClose,
  onRename,
  onFork,
  onArchive,
  onOpenProject,
  onStop,
}: {
  visible: boolean
  title: string
  ready: boolean
  canFork: boolean
  stoppable: boolean
  onClose: () => void
  onRename: (title: string) => void
  onFork: () => void
  onArchive: () => void
  onOpenProject: () => void
  onStop: () => void
}) {
  const { t } = useI18n()
  const [renaming, setRenaming] = useState(false)
  const close = () => {
    setRenaming(false)
    onClose()
  }
  const pick = (action: () => void) => () => {
    close()
    action()
  }
  return (
    <SheetLayer visible={visible} onClose={close}>
      <SheetHeader title={renaming ? t('chat.rename') : title} onClose={close} />
      {renaming ? (
        <RenameForm
          title={title}
          onSave={(name) => {
            close()
            onRename(name)
          }}
        />
      ) : (
        <View style={styles.menu}>
          <MenuRow icon="pencil" label={t('chat.rename')} disabled={!ready} onPress={() => setRenaming(true)} />
          {canFork ? <MenuRow icon="gitFork" label={t('chat.fork')} disabled={!ready} onPress={pick(onFork)} /> : null}
          <MenuRow icon="archive" label={t('chat.archive')} disabled={!ready} onPress={pick(onArchive)} />
          <Separator />
          <MenuRow icon="folder" label={t('chat.openProject')} onPress={pick(onOpenProject)} />
          {stoppable ? <MenuRow icon="circleStop" label={t('composer.stop')} onPress={pick(onStop)} /> : null}
        </View>
      )}
    </SheetLayer>
  )
}

const styles = StyleSheet.create({
  menu: { marginHorizontal: -8 },
  row: { alignItems: 'flex-start', borderRadius: metrics.rowRadius, paddingHorizontal: 12 },
  separator: { height: StyleSheet.hairlineWidth, marginVertical: 4, marginHorizontal: 12 },
  input: { height: metrics.touch, paddingHorizontal: 16, borderWidth: 1, borderRadius: metrics.pill, outlineWidth: 0 },
})
