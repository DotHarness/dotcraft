import { Pressable, StyleSheet, View } from 'react-native'
import Svg, { Circle } from 'react-native-svg'
import { useI18n } from '../../i18n'
import { useTheme } from '../theme'

const SIZE = 16
const STROKE = 2
const RADIUS = (SIZE - STROKE) / 2
const CIRCUMFERENCE = 2 * Math.PI * RADIUS

export function ContextRing({ used, onPress }: { used: number | null; onPress: () => void }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const filled = used ?? 0
  return (
    <Pressable
      accessibilityRole="button"
      accessibilityLabel={t('chatStatus.open')}
      accessibilityValue={used === null ? undefined : { text: t('chatStatus.used', { percent: Math.round(filled * 100) }) }}
      onPress={onPress}
      hitSlop={4}
      style={({ pressed }) => [styles.button, pressed && { backgroundColor: colors.roundFillPressed }]}
    >
      <View style={styles.turned}>
        <Svg width={SIZE} height={SIZE} viewBox={`0 0 ${SIZE} ${SIZE}`}>
          <Circle cx={SIZE / 2} cy={SIZE / 2} r={RADIUS} fill="none" stroke={colors.borderActive} strokeWidth={STROKE} />
          {filled > 0 ? (
            <Circle
              cx={SIZE / 2}
              cy={SIZE / 2}
              r={RADIUS}
              fill="none"
              stroke={colors.textSecondary}
              strokeWidth={STROKE}
              strokeLinecap="round"
              strokeDasharray={`${CIRCUMFERENCE * filled} ${CIRCUMFERENCE}`}
            />
          ) : null}
        </Svg>
      </View>
    </Pressable>
  )
}

const styles = StyleSheet.create({
  button: { width: 40, height: 40, borderRadius: 20, alignItems: 'center', justifyContent: 'center' },
  turned: { transform: [{ rotate: '-90deg' }] },
})
