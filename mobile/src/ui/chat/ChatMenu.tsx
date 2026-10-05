import * as Clipboard from 'expo-clipboard'
import { useState } from 'react'
import { StyleSheet, TextInput } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { useI18n } from '../../i18n'
import { MenuRow, PopoverMenu } from '../Menu'
import { PhoneButton } from '../parts'
import { SheetHeader, SheetLayer } from '../Sheet'
import { metrics, type, useTheme } from '../theme'
import { BAR_HEIGHT } from './ChatBar'

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
  chatId,
  ready,
  canFork,
  onClose,
  onRename,
  onFork,
  onArchive,
}: {
  visible: boolean
  title: string
  chatId: string
  ready: boolean
  canFork: boolean
  onClose: () => void
  onRename: (title: string) => void
  onFork: () => void
  onArchive: () => void
}) {
  const { t } = useI18n()
  const insets = useSafeAreaInsets()
  const [renaming, setRenaming] = useState(false)
  const pick = (action: () => void) => () => {
    onClose()
    action()
  }
  return (
    <>
      <PopoverMenu
        visible={visible}
        label={t('chat.menu')}
        title={title}
        anchor={{ top: insets.top + BAR_HEIGHT + 12 }}
        align="end"
        onClose={onClose}
      >
        <MenuRow icon="copy" label={t('chat.copyId')} onPress={pick(() => void Clipboard.setStringAsync(chatId))} />
        <MenuRow icon="pencil" label={t('chat.rename')} disabled={!ready} onPress={pick(() => setRenaming(true))} />
        {canFork ? <MenuRow icon="gitFork" label={t('chat.fork')} disabled={!ready} onPress={pick(onFork)} /> : null}
        <MenuRow icon="archive" label={t('chat.archive')} danger disabled={!ready} onPress={pick(onArchive)} />
      </PopoverMenu>
      <SheetLayer visible={renaming} onClose={() => setRenaming(false)}>
        <SheetHeader title={t('chat.rename')} />
        {renaming ? (
          <RenameForm
            title={title}
            onSave={(name) => {
              setRenaming(false)
              onRename(name)
            }}
          />
        ) : null}
      </SheetLayer>
    </>
  )
}

const styles = StyleSheet.create({
  input: { height: metrics.touch, paddingHorizontal: 16, borderWidth: 1, borderRadius: metrics.pill, outlineWidth: 0 },
})
