import { useEffect, useState } from 'react'
import { Animated, Easing, Pressable, StyleSheet, View } from 'react-native'
import Svg, { Circle } from 'react-native-svg'
import type { TodoCard as TodoCardModel, TodoItem } from '../../core/todoCard'
import { useI18n, type I18n } from '../../i18n'
import { Icon, type IconName } from '../icons'
import { nativeDriver, Txt } from '../parts'
import { useTheme } from '../theme'

const FOLD_AFTER = 6
const ICON_SIZE = 15

const STATUS_ICONS: Record<TodoItem['status'], IconName> = {
  pending: 'circle',
  in_progress: 'circleDot',
  completed: 'circleCheck',
  cancelled: 'circleX',
}

function foldedPrefix(todos: readonly TodoItem[]): number {
  if (todos.length <= FOLD_AFTER) return 0
  const firstOpen = todos.findIndex((todo) => todo.status === 'pending' || todo.status === 'in_progress')
  const prefix = firstOpen < 0 ? todos.length : firstOpen
  return prefix >= 2 ? prefix : 0
}

function statusLabel(t: I18n['t'], status: TodoItem['status'], live: boolean): string {
  switch (status) {
    case 'completed': return t('todo.status.completed')
    case 'cancelled': return t('todo.status.cancelled')
    case 'in_progress': return t(live ? 'todo.status.active' : 'todo.status.stopped')
    default: return t('todo.status.pending')
  }
}

function TodoSpinner() {
  const { colors } = useTheme()
  const [rotation] = useState(() => new Animated.Value(0))
  useEffect(() => {
    const loop = Animated.loop(Animated.timing(rotation, { toValue: 1, duration: 1600, easing: Easing.linear, useNativeDriver: nativeDriver }))
    loop.start()
    return () => loop.stop()
  }, [rotation])
  const rotate = rotation.interpolate({ inputRange: [0, 1], outputRange: ['0deg', '360deg'] })
  return (
    <Animated.View style={{ width: ICON_SIZE, height: ICON_SIZE, transform: [{ rotate }] }}>
      <Svg width={ICON_SIZE} height={ICON_SIZE} viewBox="0 0 24 24" fill="none">
        <Circle cx={12} cy={12} r={10} stroke={colors.borderDefault} strokeWidth={1.8} />
        <Circle cx={12} cy={12} r={10} stroke={colors.textSecondary} strokeWidth={1.8} strokeDasharray="47 63" strokeLinecap="round" />
      </Svg>
    </Animated.View>
  )
}

export function TodoCard({ card }: { card: TodoCardModel }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const [unfolded, setUnfolded] = useState(false)
  const { todos, live } = card
  const folded = unfolded ? 0 : foldedPrefix(todos)
  const counted = todos.filter((todo) => todo.status !== 'cancelled')
  const done = counted.filter((todo) => todo.status === 'completed').length
  return (
    <View
      accessibilityLabel={`${t('todo.label')}, ${t('todo.progress', { done, total: counted.length })}`}
      style={[styles.card, { borderColor: colors.borderDefault }]}
    >
      {folded > 0 ? (
        <Pressable accessibilityRole="button" onPress={() => setUnfolded(true)} style={styles.row}>
          <View style={styles.mark}>
            <Icon name="chevronRight" size={ICON_SIZE} color={colors.textDimmed} />
          </View>
          <Txt tone="dimmed" style={styles.text}>{t('todo.foldedDone', { count: folded })}</Txt>
        </Pressable>
      ) : null}
      {todos.slice(folded).map((todo) => {
        const active = todo.status === 'in_progress'
        const quiet = todo.status === 'completed' || todo.status === 'cancelled'
        return (
          <View key={todo.id} accessible accessibilityLabel={`${todo.content}, ${statusLabel(t, todo.status, live)}`} style={styles.row}>
            <View style={styles.mark}>
              {active && live
                ? <TodoSpinner />
                : <Icon name={STATUS_ICONS[todo.status]} size={ICON_SIZE} color={active ? colors.textPrimary : colors.textDimmed} />}
            </View>
            <Txt tone={active ? 'primary' : quiet ? 'dimmed' : 'secondary'} style={[styles.text, todo.status === 'cancelled' && styles.struck]}>
              {live && active ? todo.activeForm ?? todo.content : todo.content}
            </Txt>
          </View>
        )
      })}
    </View>
  )
}

const styles = StyleSheet.create({
  card: { gap: 6, padding: 12, borderWidth: 1, borderRadius: 8 },
  row: { flexDirection: 'row', alignItems: 'flex-start', gap: 8 },
  mark: { width: 16, height: 20, alignItems: 'center', justifyContent: 'center' },
  text: { flexShrink: 1 },
  struck: { textDecorationLine: 'line-through' },
})
