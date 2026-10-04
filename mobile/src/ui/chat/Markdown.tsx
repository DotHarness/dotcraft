import type { ListItem, PhrasingContent, Root, RootContent, Table } from 'mdast'
import { fromMarkdown } from 'mdast-util-from-markdown'
import { gfmFromMarkdown } from 'mdast-util-gfm'
import { mathFromMarkdown } from 'mdast-util-math'
import { gfm } from 'micromark-extension-gfm'
import { memo, useMemo, type ReactNode } from 'react'
import { ScrollView, StyleSheet, Text, View, type TextStyle } from 'react-native'
import { resolveLink } from '../../core/links'
import { Icon } from '../icons'
import { metrics, type, useTheme } from '../theme'
import { FileChip, InlineChip, LinkChip } from './Chips'
import { mathSyntax } from './mathSyntax'

interface Env {
  colors: ReturnType<typeof useTheme>['colors']
  workspacePath: string | null
}

const HEADING: Record<number, TextStyle> = {
  1: { fontSize: 20, lineHeight: 27, fontWeight: '700' },
  2: { fontSize: 18, lineHeight: 25, fontWeight: '600' },
  3: { fontSize: 16, lineHeight: 23, fontWeight: '600' },
}

function plain(nodes: PhrasingContent[]): string {
  return nodes
    .map((node) => ('value' in node ? node.value : 'children' in node ? plain(node.children as PhrasingContent[]) : ''))
    .join('')
}

function inline(nodes: PhrasingContent[], env: Env, key = ''): ReactNode[] {
  const { colors } = env
  return nodes.map((node, index) => {
    const id = `${key}${index}`
    switch (node.type) {
      case 'text':
      case 'html':
        return node.value
      case 'strong':
        return (
          <Text key={id} style={styles.strong}>
            {inline(node.children, env, `${id}.`)}
          </Text>
        )
      case 'emphasis':
        return (
          <Text key={id} style={styles.emphasis}>
            {inline(node.children, env, `${id}.`)}
          </Text>
        )
      case 'delete':
        return (
          <Text key={id} style={styles.delete}>
            {inline(node.children, env, `${id}.`)}
          </Text>
        )
      case 'inlineCode':
      case 'inlineMath':
        return (
          <Text key={id} style={[type.code, { backgroundColor: colors.bgTertiary }]}>
            {` ${node.value} `}
          </Text>
        )
      case 'break':
        return '\n'
      case 'link': {
        const target = resolveLink(node.url, plain(node.children), env.workspacePath)
        if (target.kind === 'reject') return target.label
        return (
          <InlineChip key={id}>
            {target.kind === 'file' ? <FileChip path={target.path} label={target.label} /> : <LinkChip url={target.url} label={target.label} />}
          </InlineChip>
        )
      }
      case 'image':
        return node.alt ?? ''
      default:
        return null
    }
  })
}

function columnWidth(table: Table, column: number): number {
  const longest = Math.max(...table.children.map((row) => plain((row.children[column]?.children ?? []) as PhrasingContent[]).length))
  return Math.min(260, Math.max(72, longest * 7.5 + 24))
}

function TableBlock({ table, env }: { table: Table; env: Env }) {
  const { colors } = env
  const columns = table.children[0]?.children.length ?? 0
  const widths = Array.from({ length: columns }, (_, column) => columnWidth(table, column))
  return (
    <ScrollView horizontal showsHorizontalScrollIndicator={false} style={[styles.tableFrame, { borderColor: colors.borderDefault }]}>
      <View>
        {table.children.map((row, rowIndex) => (
          <View
            key={rowIndex}
            style={[styles.tableRow, rowIndex > 0 && { borderTopWidth: 1, borderTopColor: colors.borderDefault }, rowIndex === 0 && { backgroundColor: colors.bgSecondary }]}
          >
            {widths.map((width, column) => (
              <Text
                key={column}
                style={[
                  type.body,
                  styles.cell,
                  { width, color: colors.textPrimary, textAlign: table.align?.[column] ?? 'left' },
                  rowIndex === 0 && styles.strong,
                ]}
              >
                {inline((row.children[column]?.children ?? []) as PhrasingContent[], env)}
              </Text>
            ))}
          </View>
        ))}
      </View>
    </ScrollView>
  )
}

function ListBlock({ items, ordered, start, env }: { items: ListItem[]; ordered: boolean; start: number; env: Env }) {
  const { colors } = env
  const base = [type.text, styles.prose, { color: colors.textPrimary }]
  return (
    <View style={styles.list}>
      {items.map((item, index) => (
        <View key={index} style={styles.listItem}>
          {item.checked === true || item.checked === false ? (
            <View style={[styles.task, { borderColor: colors.borderActive }, item.checked && { backgroundColor: colors.textPrimary, borderColor: colors.textPrimary }]}>
              {item.checked ? <Icon name="check" size={11} color={colors.bgPrimary} strokeWidth={3} /> : null}
            </View>
          ) : (
            <Text style={[base, styles.marker, { color: colors.textSecondary }]}>{ordered ? `${start + index}.` : '•'}</Text>
          )}
          <View style={styles.itemBody}>{blocks(item.children, env)}</View>
        </View>
      ))}
    </View>
  )
}

function block(node: RootContent, env: Env, key: number): ReactNode {
  const { colors } = env
  const base = [type.text, styles.prose, { color: colors.textPrimary }]
  switch (node.type) {
    case 'paragraph':
      return (
        <Text key={key} selectable style={base}>
          {inline(node.children, env)}
        </Text>
      )
    case 'heading':
      return (
        <Text key={key} accessibilityRole="header" style={[base, HEADING[node.depth] ?? styles.strong]}>
          {inline(node.children, env)}
        </Text>
      )
    case 'list':
      return <ListBlock key={key} items={node.children} ordered={node.ordered === true} start={node.start ?? 1} env={env} />
    case 'blockquote':
      return (
        <View key={key} style={[styles.quote, { borderLeftColor: colors.borderActive }]}>
          {blocks(node.children, env)}
        </View>
      )
    case 'code':
    case 'math':
      return (
        <View key={key} style={[styles.code, { backgroundColor: colors.bgTertiary }]}>
          <ScrollView horizontal showsHorizontalScrollIndicator={false} contentContainerStyle={styles.codeContent}>
            <Text selectable style={[type.code, styles.codeText, { color: colors.textPrimary }]}>
              {node.value}
            </Text>
          </ScrollView>
        </View>
      )
    case 'table':
      return <TableBlock key={key} table={node} env={env} />
    case 'thematicBreak':
      return <View key={key} style={[styles.rule, { backgroundColor: colors.borderDefault }]} />
    case 'html':
      return (
        <Text key={key} selectable style={base}>
          {node.value}
        </Text>
      )
    default:
      return null
  }
}

function blocks(nodes: RootContent[], env: Env): ReactNode {
  return nodes.map((node, index) => block(node, env, index))
}

function parse(text: string): Root {
  return fromMarkdown(text, { extensions: [gfm(), mathSyntax()], mdastExtensions: [gfmFromMarkdown(), mathFromMarkdown()] })
}

export const Markdown = memo(function Markdown({ text, workspacePath }: { text: string; workspacePath: string | null }) {
  const { colors } = useTheme()
  const tree = useMemo(() => parse(text), [text])
  return <View style={styles.blocks}>{blocks(tree.children, { colors, workspacePath })}</View>
})

const styles = StyleSheet.create({
  blocks: { gap: 10 },
  prose: { lineHeight: 22 },
  strong: { fontWeight: '700' },
  emphasis: { fontStyle: 'italic' },
  delete: { textDecorationLine: 'line-through' },
  list: { gap: 4 },
  listItem: { flexDirection: 'row', gap: 8, paddingLeft: 2 },
  marker: { minWidth: 14 },
  itemBody: { flex: 1, minWidth: 0, gap: 6 },
  task: { width: 16, height: 16, marginTop: 3, borderWidth: 1.5, borderRadius: 4, alignItems: 'center', justifyContent: 'center' },
  quote: { gap: 8, paddingLeft: 12, borderLeftWidth: 3 },
  code: { borderRadius: 12, overflow: 'hidden' },
  codeContent: { padding: 12 },
  codeText: { lineHeight: 20 },
  tableFrame: { borderWidth: 1, borderRadius: metrics.listRadius },
  tableRow: { flexDirection: 'row' },
  cell: { paddingVertical: 7, paddingHorizontal: 10 },
  rule: { height: 1, marginVertical: 4 },
})
