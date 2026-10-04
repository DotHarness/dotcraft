import { Pressable, ScrollView, StyleSheet, Text, View } from 'react-native'
import type { ReferenceEntry } from '../../core/draft'
import { useI18n } from '../../i18n'
import { Icon } from '../icons'
import { Txt } from '../parts'
import { metrics, type, useTheme } from '../theme'

function EntryRow({ entry, onPress }: { entry: ReferenceEntry; onPress: () => void }) {
  const { colors } = useTheme()
  const label = entry.kind === 'command' ? `/${entry.name}` : `$${entry.name}`
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={label}
      onPress={onPress}
      style={({ pressed }) => [styles.row, pressed && { backgroundColor: colors.roundFill }]}
    >
      <Icon name={entry.kind === 'command' ? 'squareTerminal' : 'box'} size={18} color={colors.textSecondary} />
      <View style={styles.text}>
        <Text numberOfLines={1} style={[type.text, styles.name, { color: colors.textPrimary }]}>
          {entry.name}
        </Text>
        {entry.description ? (
          <Txt variant="meta" tone="secondary" numberOfLines={1}>
            {entry.description}
          </Txt>
        ) : null}
      </View>
    </Pressable>
  )
}

export function ReferencePicker({ entries, onChoose }: { entries: ReferenceEntry[]; onChoose: (entry: ReferenceEntry) => void }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const commands = entries.filter((entry) => entry.kind === 'command')
  const skills = entries.filter((entry) => entry.kind === 'skill')
  const section = (title: string, list: ReferenceEntry[]) =>
    list.length > 0 ? (
      <View key={title}>
        <Txt variant="meta" tone="secondary" style={styles.title}>
          {title}
        </Txt>
        {list.map((entry) => (
          <EntryRow key={`${entry.kind}:${entry.name}`} entry={entry} onPress={() => onChoose(entry)} />
        ))}
      </View>
    ) : null
  return (
    <View
      accessibilityLabel={t('references.label')}
      style={[styles.card, { borderColor: colors.borderSubtle, backgroundColor: colors.bgElevated, boxShadow: colors.shadow3 }]}
    >
      <ScrollView keyboardShouldPersistTaps="always" showsVerticalScrollIndicator={false} contentContainerStyle={styles.list}>
        {section(t('references.commands'), commands)}
        {section(t('references.skills'), skills)}
      </ScrollView>
    </View>
  )
}

const styles = StyleSheet.create({
  card: { maxHeight: 248, borderWidth: 1, borderRadius: metrics.heroRadius, overflow: 'hidden' },
  list: { padding: 6, gap: 4 },
  title: { paddingTop: 6, paddingBottom: 2, paddingHorizontal: 12, fontWeight: '600' },
  row: { flexDirection: 'row', alignItems: 'center', gap: 12, minHeight: 48, paddingVertical: 6, paddingHorizontal: 12, borderRadius: metrics.rowRadius },
  text: { flex: 1, minWidth: 0, gap: 1 },
  name: { fontWeight: '500' },
})
