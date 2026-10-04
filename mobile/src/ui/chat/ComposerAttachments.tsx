import { Image, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native'
import type { FileAttachment, PhotoAttachment } from '../../core/draft'
import { useI18n } from '../../i18n'
import { Icon } from '../icons'
import { type, useTheme } from '../theme'

function RemoveButton({ label, onPress }: { label: string; onPress: () => void }) {
  const { colors } = useTheme()
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      onPress={onPress}
      hitSlop={6}
      style={({ pressed }) => [styles.remove, { backgroundColor: pressed ? colors.textSecondary : colors.overlayScrim }]}
    >
      <Icon name="x" size={12} color="#ffffff" strokeWidth={2.4} />
    </Pressable>
  )
}

export function ComposerAttachments({
  photos,
  files,
  onRemovePhoto,
  onRemoveFile,
}: {
  photos: PhotoAttachment[]
  files: FileAttachment[]
  onRemovePhoto: (id: string) => void
  onRemoveFile: (id: string) => void
}) {
  const { t } = useI18n()
  const { colors } = useTheme()
  if (photos.length === 0 && files.length === 0) return null
  return (
    <ScrollView horizontal showsHorizontalScrollIndicator={false} keyboardShouldPersistTaps="handled" contentContainerStyle={styles.row}>
      {photos.map((photo, index) => {
        const name = t('composer.photoName', { index: index + 1 })
        return (
          <View key={photo.id} style={styles.tile}>
            <Image
              accessibilityLabel={name}
              source={{ uri: photo.dataUrl }}
              resizeMode="cover"
              style={[styles.photo, { borderColor: colors.borderDefault, backgroundColor: colors.bgTertiary }]}
            />
            <RemoveButton label={t('composer.remove', { name })} onPress={() => onRemovePhoto(photo.id)} />
          </View>
        )
      })}
      {files.map((file) => (
        <View key={file.id} style={[styles.file, { borderColor: colors.borderDefault, backgroundColor: colors.bgSecondary }]}>
          <View style={[styles.fileIcon, { backgroundColor: colors.roundFill }]}>
            <Icon name="file" size={18} color={colors.textSecondary} />
          </View>
          <Text numberOfLines={2} style={[type.meta, styles.fileName, { color: colors.textPrimary }]}>
            {file.name}
          </Text>
          <RemoveButton label={t('composer.remove', { name: file.name })} onPress={() => onRemoveFile(file.id)} />
        </View>
      ))}
    </ScrollView>
  )
}

const TILE = 60

const styles = StyleSheet.create({
  row: { gap: 8, paddingHorizontal: 4, paddingTop: 4, paddingBottom: 2 },
  tile: { width: TILE, height: TILE },
  photo: { width: TILE, height: TILE, borderWidth: 1, borderRadius: 12 },
  remove: { position: 'absolute', top: 4, right: 4, width: 20, height: 20, borderRadius: 10, alignItems: 'center', justifyContent: 'center' },
  file: { flexDirection: 'row', alignItems: 'center', gap: 10, width: 196, height: TILE, paddingLeft: 10, paddingRight: 30, borderWidth: 1, borderRadius: 12 },
  fileIcon: { width: 36, height: 36, borderRadius: 10, alignItems: 'center', justifyContent: 'center' },
  fileName: { flex: 1, minWidth: 0, fontWeight: '500' },
})
