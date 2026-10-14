import { useEffect, useMemo, useState } from 'react'
import { StyleSheet, Text, View } from 'react-native'
import { fileContent, fileFailure, isMarkdownPath, mediaTypeOf, type FileContent, type FileFailure } from '../../core/fileView'
import { parseFrontmatter, type FrontmatterEntry } from '../../core/frontmatter'
import { baseName } from '../../core/transcript'
import { useI18n } from '../../i18n'
import { RoundIconButton, Spinner } from '../parts'
import { SheetHeader, SheetLayer } from '../Sheet'
import { type, useTheme } from '../theme'
import { FilePath } from './Chips'
import { DownloadButton } from './DownloadButton'
import { ImageThumb } from './Images'
import { Markdown } from './Markdown'

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
      {blocks.map((block, index) => (
        <Text key={index} selectable style={[type.code, styles.code, { color: colors.textPrimary }]}>
          {block}
        </Text>
      ))}
    </View>
  )
}

function FrontmatterCard({ entries }: { entries: FrontmatterEntry[] }) {
  const { colors } = useTheme()
  return (
    <View style={[styles.frontmatter, { borderColor: colors.borderDefault, backgroundColor: colors.bgSecondary }]}>
      {entries.map(({ key, value }) => (
        <View key={key} style={styles.entry}>
          <Text numberOfLines={1} style={[type.code, styles.entryKey, { color: colors.textSecondary }]}>
            {key}
          </Text>
          {Array.isArray(value) ? (
            <View style={styles.tags}>
              {value.map((item, index) => (
                <Text key={index} style={[type.code, styles.tag, { color: colors.textPrimary, backgroundColor: colors.roundFill }]}>
                  {item}
                </Text>
              ))}
            </View>
          ) : (
            <Text selectable style={[type.body, styles.entryValue, { color: colors.textPrimary }]}>
              {value}
            </Text>
          )}
        </View>
      ))}
    </View>
  )
}

function MarkdownFile({ text, workspacePath }: { text: string; workspacePath: string | null }) {
  const frontmatter = useMemo(() => parseFrontmatter(text), [text])
  return (
    <View style={styles.markdown}>
      {frontmatter ? <FrontmatterCard entries={frontmatter.entries} /> : null}
      <Markdown text={frontmatter ? text.slice(frontmatter.end) : text} workspacePath={workspacePath} />
    </View>
  )
}

function FileBody({ path, result, preview, workspacePath }: { path: string; result: Result; preview: boolean; workspacePath: string | null }) {
  const { t } = useI18n()
  if (result.state === 'loading') {
    return (
      <View style={styles.loading}>
        <Spinner size={22} />
      </View>
    )
  }
  if (result.state === 'ready' && result.content.kind === 'text') {
    return preview ? <MarkdownFile text={result.content.text} workspacePath={workspacePath} /> : <TextFile text={result.content.text} />
  }
  if (result.state === 'ready' && result.content.kind === 'image')
    return <ImageThumb uri={result.content.uri} name={baseName(path)} label={t('image.open')} style={styles.image} />
  const note = result.state === 'failed' ? t(result.failure === 'tooLarge' ? 'file.tooLarge' : 'file.failed') : t('file.unsupported')
  return <FilePath path={path} note={note} />
}

function OpenFile({ path, read, workspacePath }: { path: string; read: (path: string) => Promise<string>; workspacePath: string | null }) {
  const { t } = useI18n()
  const [result, setResult] = useState<Result>({ state: 'loading' })
  const [source, setSource] = useState(false)
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
  const markdown = result.state === 'ready' && result.content.kind === 'text' && isMarkdownPath(path)
  return (
    <>
      <SheetHeader
        title={name}
        action={
          result.state === 'ready' ? (
            <View style={styles.actions}>
              {markdown ? (
                <RoundIconButton
                  label={t(source ? 'file.viewPreview' : 'file.viewSource')}
                  icon={source ? 'bookOpen' : 'code'}
                  onPress={() => setSource((value) => !value)}
                />
              ) : null}
              <DownloadButton name={name} mimeType={mediaTypeOf(path)} data={result.data} />
            </View>
          ) : null
        }
      />
      <FileBody path={path} result={result} preview={markdown && !source} workspacePath={workspacePath} />
    </>
  )
}

export function FileSheet({
  path,
  read,
  workspacePath,
  onClose,
}: {
  path: string | null
  read: (path: string) => Promise<string>
  workspacePath: string | null
  onClose: () => void
}) {
  return (
    <SheetLayer visible={path !== null} onClose={onClose}>
      {path ? <OpenFile key={path} path={path} read={read} workspacePath={workspacePath} /> : null}
    </SheetLayer>
  )
}

const styles = StyleSheet.create({
  loading: { height: 120, alignItems: 'center', justifyContent: 'center' },
  actions: { flexDirection: 'row', gap: 8 },
  text: { padding: 12, borderRadius: 12, overflow: 'hidden' },
  code: { lineHeight: 20 },
  image: { width: '100%' },
  markdown: { gap: 14 },
  frontmatter: { gap: 6, padding: 12, borderWidth: 1, borderRadius: 12 },
  entry: { flexDirection: 'row', gap: 12 },
  entryKey: { width: 104, lineHeight: 21 },
  entryValue: { flex: 1, minWidth: 0 },
  tags: { flex: 1, minWidth: 0, flexDirection: 'row', flexWrap: 'wrap', gap: 4, paddingTop: 1 },
  tag: { paddingHorizontal: 6, borderRadius: 4, lineHeight: 19, overflow: 'hidden' },
})
