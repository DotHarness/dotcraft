import { createContext, useContext, useState } from 'react'
import { Pressable, StyleSheet, Text, View } from 'react-native'
import { baseName } from '../../core/transcript'
import type { DiffHunk, FileChange, TurnChanges } from '../../core/turnChanges'
import { useI18n } from '../../i18n'
import { Icon } from '../icons'
import { Txt } from '../parts'
import { SheetHeader, SheetLayer } from '../Sheet'
import { metrics, type, useTheme } from '../theme'

function useFilesLabel(): (count: number) => string {
  const { t } = useI18n()
  return (count) => t(count === 1 ? 'changes.filesOne' : 'changes.filesMany', { count })
}

function Counts({ added, removed, variant = 'text' }: { added: number; removed: number; variant?: 'text' | 'meta' }) {
  const { colors } = useTheme()
  return (
    <View style={styles.counts}>
      <Txt variant={variant} style={[styles.count, { color: colors.successText }]}>{`+${added}`}</Txt>
      <Txt variant={variant} style={[styles.count, { color: colors.errorText }]}>{`−${removed}`}</Txt>
    </View>
  )
}

export function ChangesPill({ changes, onPress }: { changes: TurnChanges; onPress: () => void }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const files = useFilesLabel()(changes.files.length)
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={`${t('changes.title')}: ${files}, +${changes.added} −${changes.removed}`}
      onPress={onPress}
      style={({ pressed }) => [
        styles.pill,
        { borderColor: colors.borderDefault, backgroundColor: pressed ? colors.bgTertiary : colors.bgElevated, boxShadow: colors.shadow1 },
      ]}
    >
      <Txt numberOfLines={1} style={styles.shrink}>
        {files}
      </Txt>
      <Counts added={changes.added} removed={changes.removed} />
    </Pressable>
  )
}

export const ChangesOpenerContext = createContext<((turnId: string) => void) | null>(null)

export function TurnChangesCard({ changes }: { changes: TurnChanges }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const open = useContext(ChangesOpenerContext)
  const title =
    changes.files.length === 1 ? t('changes.editedFile', { file: baseName(changes.files[0].path) }) : t('changes.editedFiles', { count: changes.files.length })
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={`${title}, +${changes.added} −${changes.removed}`}
      disabled={!open}
      onPress={() => open?.(changes.turnId)}
      style={({ pressed }) => [styles.card, { borderColor: colors.borderDefault, backgroundColor: pressed ? colors.bgTertiary : colors.bgSecondary }]}
    >
      <View style={styles.cardText}>
        <Txt numberOfLines={1} style={styles.name}>
          {title}
        </Txt>
        <Counts added={changes.added} removed={changes.removed} variant="meta" />
      </View>
      <Icon name="chevronRight" size={16} color={colors.textSecondary} />
    </Pressable>
  )
}

function Hunk({ hunk }: { hunk: DiffHunk }) {
  const { colors } = useTheme()
  const code = [type.code, styles.code]
  return (
    <>
      <View style={[styles.line, { backgroundColor: colors.roundFill }]}>
        <Text style={[...code, styles.text, { color: colors.textDimmed }]}>{hunk.header}</Text>
      </View>
      {hunk.lines.map((line, index) => {
        const tint = line.kind === 'add' ? colors.success : line.kind === 'remove' ? colors.error : null
        const marker = line.kind === 'add' ? '+' : line.kind === 'remove' ? '-' : ' '
        return (
          <View key={index} style={styles.line}>
            {tint ? <View style={[StyleSheet.absoluteFill, styles.tint, { backgroundColor: tint }]} /> : null}
            <Text style={[...code, styles.number, { color: colors.textDimmed }]}>{line.newLine ?? line.oldLine}</Text>
            <Text style={[...code, styles.marker, { color: tint ? (line.kind === 'add' ? colors.successText : colors.errorText) : colors.textDimmed }]}>
              {marker}
            </Text>
            <Text style={[...code, styles.text, { color: colors.textPrimary }]}>{line.text}</Text>
          </View>
        )
      })}
    </>
  )
}

function DiffView({ file }: { file: FileChange }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  if (file.hunks.length === 0) {
    return (
      <Txt variant="meta" tone="secondary" style={styles.noDiff}>
        {t('changes.noDiff')}
      </Txt>
    )
  }
  return (
    <View style={[styles.diff, { borderColor: colors.borderDefault }]}>
      {file.hunks.map((hunk, index) => (
        <Hunk key={index} hunk={hunk} />
      ))}
    </View>
  )
}

function FileRow({ file, onOpen }: { file: FileChange; onOpen: ((path: string) => void) | null }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const [open, setOpen] = useState(false)
  const slash = file.path.lastIndexOf('/')
  const name = file.path.slice(slash + 1)
  const folder = slash > 0 ? file.path.slice(0, slash) : ''
  return (
    <View style={[styles.file, open && { backgroundColor: colors.bgTertiary }]}>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={t('changes.diffOf', { file: name })}
        accessibilityState={{ expanded: open }}
        onPress={() => setOpen((value) => !value)}
        style={({ pressed }) => [styles.fileRow, pressed && { backgroundColor: colors.roundFill }]}
      >
        <View style={styles.fileName}>
          {onOpen ? (
            <Pressable accessibilityRole="link" accessibilityLabel={t('file.open', { file: name })} hitSlop={4} onPress={() => onOpen(file.path)}>
              <Txt numberOfLines={1} style={styles.name}>
                {name}
              </Txt>
            </Pressable>
          ) : (
            <Txt numberOfLines={1} style={styles.name}>
              {name}
            </Txt>
          )}
          {folder ? (
            <Txt variant="caption" tone="dimmed" numberOfLines={1} ellipsizeMode="head">
              {folder}
            </Txt>
          ) : null}
        </View>
        <Counts added={file.added} removed={file.removed} variant="meta" />
        <View style={[styles.chevron, open && styles.turned]}>
          <Icon name="chevronRight" size={16} color={colors.textSecondary} />
        </View>
      </Pressable>
      {open ? <DiffView file={file} /> : null}
    </View>
  )
}

export function ChangesSheet({
  changes,
  visible,
  onClose,
  onOpenFile,
}: {
  changes: TurnChanges
  visible: boolean
  onClose: () => void
  onOpenFile: ((path: string) => void) | null
}) {
  const files = useFilesLabel()(changes.files.length)
  return (
    <SheetLayer visible={visible} onClose={onClose}>
      <SheetHeader title={files} />
      <Counts added={changes.added} removed={changes.removed} variant="meta" />
      <View style={styles.files}>
        {changes.files.map((file) => (
          <FileRow key={`${changes.turnId}:${file.path}`} file={file} onOpen={onOpenFile} />
        ))}
      </View>
    </SheetLayer>
  )
}

const styles = StyleSheet.create({
  shrink: { flexShrink: 1 },
  counts: { flexDirection: 'row', gap: 8 },
  count: { fontVariant: ['tabular-nums'], fontWeight: '500' },
  pill: {
    alignSelf: 'center',
    flexDirection: 'row',
    alignItems: 'center',
    gap: 14,
    maxWidth: '100%',
    height: 38,
    paddingHorizontal: 18,
    borderWidth: 1,
    borderRadius: metrics.pill,
  },
  card: { flexDirection: 'row', alignItems: 'center', gap: 8, paddingVertical: 10, paddingHorizontal: 12, borderWidth: 1, borderRadius: 8 },
  cardText: { flex: 1, minWidth: 0, gap: 2 },
  files: { marginHorizontal: -12, gap: 2 },
  file: { borderRadius: metrics.rowRadius, overflow: 'hidden' },
  fileRow: { flexDirection: 'row', alignItems: 'center', gap: 8, minHeight: metrics.touch, paddingVertical: 8, paddingHorizontal: 12 },
  fileName: { flex: 1, minWidth: 0 },
  name: { fontWeight: '500' },
  chevron: { width: 16, alignItems: 'center' },
  turned: { transform: [{ rotate: '90deg' }] },
  diff: { paddingVertical: 6, borderTopWidth: StyleSheet.hairlineWidth },
  line: { flexDirection: 'row', alignItems: 'flex-start', paddingHorizontal: 12 },
  tint: { opacity: 0.14 },
  code: { lineHeight: 19 },
  number: { width: 32, marginRight: 8, textAlign: 'right' },
  marker: { width: 14 },
  text: { flex: 1, minWidth: 0 },
  noDiff: { paddingHorizontal: 12, paddingBottom: 10 },
})
