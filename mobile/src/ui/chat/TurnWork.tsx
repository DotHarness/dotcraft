import { createContext, useCallback, useContext, useEffect, useState } from 'react'
import { StyleSheet, View } from 'react-native'
import { useI18n } from '../../i18n'
import { PhoneButton, Txt } from '../parts'
import { CatchingUpRows } from './CatchingUpRows'

/** Loads the full Items of a summarized Turn of the open chat. */
export const TurnWorkContext = createContext<((turnId: string) => Promise<void>) | null>(null)

export function TurnWork({ turnId }: { turnId: string }) {
  const { t } = useI18n()
  const load = useContext(TurnWorkContext)
  const [failed, setFailed] = useState(false)
  const read = useCallback(() => load?.(turnId).catch(() => setFailed(true)), [load, turnId])
  useEffect(() => {
    void read()
  }, [read])
  const run = () => {
    setFailed(false)
    void read()
  }

  if (!failed) return <CatchingUpRows />
  return (
    <View accessibilityRole="alert" style={styles.failed}>
      <Txt variant="meta" tone="secondary" style={styles.shrink}>
        {t('activity.workFailed')}
      </Txt>
      <PhoneButton variant="outline" compact onPress={run}>
        {t('failure.retry')}
      </PhoneButton>
    </View>
  )
}

const styles = StyleSheet.create({
  failed: { flexDirection: 'row', alignItems: 'center', gap: 8 },
  shrink: { flexShrink: 1 },
})
