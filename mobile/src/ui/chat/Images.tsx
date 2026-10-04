import { createContext, useContext, useEffect, useState } from 'react'
import { Image, Modal, Pressable, StyleSheet, View, type ImageStyle, type StyleProp } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { fileContent } from '../../core/fileView'
import { rememberedImage, rememberImage } from '../../core/imageCache'
import type { ImageSource } from '../../core/userSegments'
import { useI18n } from '../../i18n'
import { RoundIconButton } from '../parts'
import { useTheme } from '../theme'

function ImageViewer({ uri, visible, onClose }: { uri: string; visible: boolean; onClose: () => void }) {
  const insets = useSafeAreaInsets()
  const { t } = useI18n()
  return (
    <Modal visible={visible} transparent animationType="fade" onRequestClose={onClose} statusBarTranslucent navigationBarTranslucent>
      <Pressable accessibilityLabel={t('common.close')} style={styles.viewer} onPress={onClose}>
        <Image source={{ uri }} resizeMode="contain" style={styles.full} />
      </Pressable>
      <View style={[styles.close, { top: insets.top + 8 }]}>
        <RoundIconButton label={t('common.close')} icon="x" tone="camera" onPress={onClose} />
      </View>
    </Modal>
  )
}

export function ImageThumb({ uri, label, style }: { uri: string; label: string; style?: StyleProp<ImageStyle> }) {
  const { colors } = useTheme()
  const [open, setOpen] = useState(false)
  const [ratio, setRatio] = useState(1)
  useEffect(() => {
    let current = true
    Image.getSize(
      uri,
      (width, height) => {
        if (current && width && height) setRatio(width / height)
      },
      () => undefined,
    )
    return () => {
      current = false
    }
  }, [uri])
  return (
    <>
      <Pressable accessibilityRole="imagebutton" accessibilityLabel={label} onPress={() => setOpen(true)}>
        <Image
          source={{ uri }}
          resizeMode="cover"
          style={[styles.thumb, { aspectRatio: ratio, borderColor: colors.borderDefault, backgroundColor: colors.bgTertiary }, style]}
        />
      </Pressable>
      <ImageViewer uri={uri} visible={open} onClose={() => setOpen(false)} />
    </>
  )
}

export const ImageReaderContext = createContext<((path: string) => Promise<string>) | null>(null)

function ComputerImage({ path, label, style }: { path: string; label: string; style?: StyleProp<ImageStyle> }) {
  const read = useContext(ImageReaderContext)
  const { colors } = useTheme()
  const [uri, setUri] = useState(() => rememberedImage(path) ?? null)
  useEffect(() => {
    if (uri || !read) return
    let current = true
    read(path).then(
      (data) => {
        const content = fileContent(path, data)
        if (!current || content.kind !== 'image') return
        rememberImage(path, content.uri)
        setUri(content.uri)
      },
      () => undefined,
    )
    return () => {
      current = false
    }
  }, [path, read, uri])
  if (uri) return <ImageThumb uri={uri} label={label} style={style} />
  return <View style={[styles.thumb, styles.pending, { borderColor: colors.borderDefault, backgroundColor: colors.bgTertiary }, style]} />
}

export function SentImage({ source, label, style }: { source: ImageSource; label: string; style?: StyleProp<ImageStyle> }) {
  return 'uri' in source ? <ImageThumb uri={source.uri} label={label} style={style} /> : <ComputerImage path={source.path} label={label} style={style} />
}

const styles = StyleSheet.create({
  viewer: { flex: 1, backgroundColor: 'rgba(0, 0, 0, 0.92)', alignItems: 'center', justifyContent: 'center' },
  full: { width: '100%', height: '100%' },
  close: { position: 'absolute', right: 12 },
  thumb: { borderWidth: 1, borderRadius: 12 },
  pending: { aspectRatio: 1 },
})
