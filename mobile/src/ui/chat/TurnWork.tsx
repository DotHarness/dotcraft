import { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react'
import { StyleSheet, View, useWindowDimensions } from 'react-native'
import { useI18n } from '../../i18n'
import { PhoneButton, Txt } from '../parts'
import { CatchingUpRows } from './CatchingUpRows'

export const TurnWorkContext = createContext<{
  load(turnId: string): Promise<void>
  subscribe(listener: () => void): () => void
} | null>(null)

export function TurnWork({ turnId }: { turnId: string }) {
  const { t } = useI18n()
  const work = useContext(TurnWorkContext)
  const [failed, setFailed] = useState(false)
  const marker = useRef<View>(null)
  const reading = useRef(false)
  const { height } = useWindowDimensions()
  const read = useCallback(async () => {
    if (!work || reading.current) return
    reading.current = true
    try { await work.load(turnId) } catch { setFailed(true) }
    finally { reading.current = false }
  }, [work, turnId])
  const visible = useCallback(() => {
    if (failed) return
    marker.current?.measureInWindow((_x, y, _width, h) => {
      if (y < height && y + h > 0) void read()
    })
  }, [failed, height, read])
  useEffect(() => work?.subscribe(visible), [work, visible])
  useEffect(() => { void read() }, [read])
  const run = () => {
    setFailed(false)
    void read()
  }

  if (!failed) return <View ref={marker}><CatchingUpRows /></View>
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
