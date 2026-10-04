import { useEffect, useMemo, useState } from 'react'
import { ScrollView, StyleSheet, Text, View } from 'react-native'
import { fileContent, fileFailure, mediaTypeOf, type FileContent, type FileFailure } from '../../core/fileView'
import { baseName } from '../../core/transcript'
import { useI18n } from '../../i18n'
import { Spinner } from '../parts'
import { SheetHeader, SheetLayer } from '../Sheet'
import { type, useTheme } from '../theme'
import { FilePath } from './Chips'
import { DownloadButton } from './DownloadButton'
import { ImageThumb } from './Images'

type Result = { state: 'loading' } | { state: 'ready'; content: FileContent; data: string } | { state: 'failed'; failure: FileFailure }

const LINES_PER_BLOCK = 200

function TextFile({ text }: { text: string }) {
  const { colors } = useTheme()
  const blocks = useMemo(() => {
    const lines = text.split(/\r?\n/)
    const out: string[] = []
    for (let index = 0; index < lines.length; index += LINES_PER_BLOCK) out.push(lines.slice(index, index + LINES_PER_BLOCK).join('\n'))
    return out
  }, [text])
  return (
    <View style={[styles.text, { backgroundColor: colors.bgTertiary }]}>
      <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.textContent}>
        <View>
          {blocks.map((block, index) => (
            <Text key={index} selectable style={[type.code, styles.code, { color: colors.textPrimary }]}>
              {block}
            </Text>
          ))}
        </View>
      </ScrollView>
    </View>
  )
}

function FileBody({ path, result }: { path: string; result: Result }) {
  const { t } = useI18n()
  if (result.state === 'loading') {
    return (
      <View style={styles.loading}>
        <Spinner size={22} />
      </View>
    )
  }
  if (result.state === 'ready' && result.content.kind === 'text') return <TextFile text={result.content.text} />
  if (result.state === 'ready' && result.content.kind === 'image')
    return <ImageThumb uri={result.content.uri} name={baseName(path)} label={t('image.open')} style={styles.image} />
  const note = result.state === 'failed' ? t(result.failure === 'tooLarge' ? 'file.tooLarge' : 'file.failed') : t('file.unsupported')
  return <FilePath path={path} note={note} />
}

function OpenFile({ path, read }: { path: string; read: (path: string) => Promise<string> }) {
  const [result, setResult] = useState<Result>({ state: 'loading' })
  useEffect(() => {
    let current = true
    read(path).then(
      (data) => {
        if (current) setResult({ state: 'ready', content: fileContent(path, data), data })
      },
      (error: unknown) => {
        if (current) setResult({ state: 'failed', failure: fileFailure(error) })
      },
    )
    return () => {
      current = false
    }
  }, [path, read])
  const name = baseName(path)
  return (
    <>
      <SheetHeader
        title={name}
        action={result.state === 'ready' ? <DownloadButton name={name} mimeType={mediaTypeOf(path)} data={result.data} /> : null}
      />
      <FileBody path={path} result={result} />
    </>
  )
}

export function FileSheet({ path, read, onClose }: { path: string | null; read: (path: string) => Promise<string>; onClose: () => void }) {
  return (
    <SheetLayer visible={path !== null} onClose={onClose}>
      {path ? <OpenFile key={path} path={path} read={read} /> : null}
    </SheetLayer>
  )
}

const styles = StyleSheet.create({
  loading: { height: 120, alignItems: 'center', justifyContent: 'center' },
  text: { borderRadius: 12, overflow: 'hidden' },
  textContent: { padding: 12 },
  code: { lineHeight: 20 },
  image: { width: '100%' },
})
