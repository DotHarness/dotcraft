import { CameraView, useCameraPermissions, type BarcodeScanningResult } from 'expo-camera'
import { useFocusEffect, useRouter } from 'expo-router'
import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react'
import { Linking, Pressable, StyleSheet, Text, View } from 'react-native'
import { useSafeAreaInsets } from 'react-native-safe-area-context'
import { StatusBar } from 'expo-status-bar'
import { useDemo, useMobileState, useSession } from '../../app-state/SessionContext'
import { parsePairingParams, parsePairingUrl, type PairingOffer } from '../../core/pairing'
import { useI18n } from '../../i18n'
import { Icon, type IconName } from '../icons'
import { Screen, TopBar } from '../layout'
import { Mascot, MascotTransition, type MascotMoment } from '../mascot/Mascot'
import { BackButton, Notice, PhoneButton, Txt } from '../parts'
import { camera, metrics, type, useTheme } from '../theme'

const FRAME = 232

function Corner({ corner }: { corner: 'tl' | 'tr' | 'bl' | 'br' }) {
  const top = corner === 'tl' || corner === 'tr'
  const left = corner === 'tl' || corner === 'bl'
  return (
    <View
      pointerEvents="none"
      style={[
        styles.corner,
        top ? { top: 0, borderTopWidth: 4 } : { bottom: 0, borderBottomWidth: 4 },
        left ? { left: 0, borderLeftWidth: 4 } : { right: 0, borderRightWidth: 4 },
        corner === 'tl' && { borderTopLeftRadius: 28 },
        corner === 'tr' && { borderTopRightRadius: 28 },
        corner === 'bl' && { borderBottomLeftRadius: 28 },
        corner === 'br' && { borderBottomRightRadius: 28 },
      ]}
    />
  )
}

function CameraArea({ active, onScan }: { active: boolean; onScan: (data: string) => void }) {
  const { t } = useI18n()
  const [permission, requestPermission] = useCameraPermissions()
  if (!permission) return null
  if (!permission.granted) {
    return (
      <View style={styles.permission}>
        <Text style={[type.meta, styles.permissionNote]}>{t('pair.cameraNote')}</Text>
        {permission.canAskAgain ? (
          <PhoneButton compact variant="outline" onPress={() => void requestPermission()}>
            {t('pair.cameraAllow')}
          </PhoneButton>
        ) : (
          <PhoneButton compact variant="outline" onPress={() => void Linking.openSettings()}>
            {t('pair.cameraSettings')}
          </PhoneButton>
        )}
      </View>
    )
  }
  if (!active) return null
  return (
    <CameraView
      style={StyleSheet.absoluteFill}
      facing="back"
      barcodeScannerSettings={{ barcodeTypes: ['qr'] }}
      onBarcodeScanned={({ data }: BarcodeScanningResult) => onScan(data)}
    />
  )
}

export function PairScanScreen({ params }: { params: Record<string, string | string[] | undefined> }) {
  const state = useMobileState()
  const session = useSession()
  const router = useRouter()
  const demo = useDemo()
  const { t } = useI18n()
  const [focused, setFocused] = useState(true)
  const [notACode, setNotACode] = useState(false)
  const handled = useRef(false)

  useFocusEffect(
    useCallback(() => {
      handled.current = false
      setFocused(true)
      return () => setFocused(false)
    }, []),
  )

  const accept = useCallback(
    (offer: PairingOffer) => {
      if (handled.current) return
      handled.current = true
      setNotACode(false)
      session.acknowledgeRevoked()
      void session.beginPairing(offer)
      router.push('/pair/allow')
    },
    [router, session],
  )

  const link = JSON.stringify(params)
  useEffect(() => {
    const offer = parsePairingParams(JSON.parse(link) as Record<string, string | string[] | undefined>)
    if (offer) accept(offer)
  }, [accept, link])

  const scan = (data: string) => {
    const offer = parsePairingUrl(data)
    if (offer) accept(offer)
    else setNotACode(true)
  }

  const revokedBy = state.revokedBy
  return (
    <Screen tone="camera" style={styles.scan}>
      {focused ? <StatusBar style="light" /> : null}
      <TopBar>{router.canGoBack() ? <BackButton onPress={() => router.back()} /> : null}</TopBar>
      {revokedBy ? (
        <Notice icon="info" tone="camera" style={styles.revoked}>
          {t('pair.revoked', { computer: revokedBy })}
        </Notice>
      ) : null}
      <Mascot moment={revokedBy ? 'sad' : 'greeting'} size={64} style={styles.scanMascot} />
      <View style={styles.scanCopy}>
        <Text accessibilityRole="header" style={[type.detailTitle, styles.white, styles.centered]}>
          {t('pair.scanTitle')}
        </Text>
        <Text style={[type.text, styles.scanNote, styles.centered]}>{t('pair.scanNote')}</Text>
      </View>
      <View style={styles.frame}>
        <View style={styles.viewfinder}>
          {demo ? (
            <Pressable
              accessibilityRole="button"
              accessibilityLabel="Simulate scanning the code"
              style={StyleSheet.absoluteFill}
              onPress={() => scan(demo.scanPayload())}
            />
          ) : (
            <CameraArea active={focused} onScan={scan} />
          )}
        </View>
        <Corner corner="tl" />
        <Corner corner="tr" />
        <Corner corner="bl" />
        <Corner corner="br" />
      </View>
      <Text style={[type.meta, styles.hint, styles.centered]}>{notACode ? t('pair.notACode') : t('pair.scanHint')}</Text>
    </Screen>
  )
}

function Outcome({
  icon,
  mascot,
  title,
  note,
  extra,
  top,
  actions,
}: {
  icon?: IconName
  mascot?: MascotMoment
  title: string
  note: string
  extra?: string
  top?: ReactNode
  actions: ReactNode
}) {
  const { colors } = useTheme()
  const insets = useSafeAreaInsets()
  return (
    <Screen>
      <TopBar>{top}</TopBar>
      <View style={styles.outcomeBody}>
        {mascot ? (
          <Mascot moment={mascot} size={88} style={styles.outcomeMascot} />
        ) : icon ? (
          <View style={[styles.badge, { backgroundColor: colors.bgTertiary }]}>
            <Icon name={icon} size={28} color={colors.textSecondary} strokeWidth={1.6} />
          </View>
        ) : null}
        <Txt accessibilityRole="header" textBreakStrategy="balanced" style={[type.detailTitle, styles.centered]}>
          {title}
        </Txt>
        <Txt tone="secondary" textBreakStrategy="balanced" style={[styles.centered, styles.outcomeNote]}>
          {note}
        </Txt>
        {extra ? <Txt style={[styles.centered, styles.extra]}>{extra}</Txt> : null}
      </View>
      <View style={[styles.actions, { paddingBottom: insets.bottom + 16 }]}>{actions}</View>
    </Screen>
  )
}

export function PairAllowScreen() {
  const state = useMobileState()
  const session = useSession()
  const router = useRouter()
  const { t } = useI18n()
  const pairing = state.pairing

  useEffect(() => {
    if (pairing.step === 'connected') router.replace('/pair/connected')
    else if (pairing.step === 'invalid') router.replace('/pair/invalid')
  }, [pairing.step, router])

  const back = () => {
    session.resetPairing()
    if (router.canGoBack()) router.back()
    else router.replace('/pair')
  }

  if (pairing.step === 'reaching') {
    return (
      <Screen>
        <TopBar>
          <BackButton onPress={back} />
        </TopBar>
        <MascotTransition line={t('pair.reaching', { computer: pairing.offer.name })} />
      </Screen>
    )
  }
  if (pairing.step === 'unreachable') {
    return (
      <Outcome
        icon="cloudOff"
        title={t('pair.unreachableTitle', { computer: pairing.offer.name })}
        note={t('pair.unreachableNote', { computer: pairing.offer.name })}
        top={<BackButton onPress={back} />}
        actions={
          <>
            <PhoneButton onPress={() => session.retryPairing()}>{t('pair.tryAgain')}</PhoneButton>
            <PhoneButton variant="ghost" onPress={back}>
              {t('pair.scanNew')}
            </PhoneButton>
          </>
        }
      />
    )
  }
  if (pairing.step !== 'allow') return <Screen>{null}</Screen>
  const current = state.computer
  const replacing = current && current.name !== pairing.offer.name ? current.name : undefined
  return (
    <Outcome
      mascot="question"
      title={t('pair.allowTitle', { computer: pairing.offer.name })}
      note={t('pair.allowNote')}
      extra={replacing ? t('pair.replaces', { computer: replacing }) : undefined}
      top={<BackButton onPress={back} />}
      actions={
        <PhoneButton loading={pairing.allowing} onPress={() => void session.allow()}>
          {t('pair.allow')}
        </PhoneButton>
      }
    />
  )
}

export function PairConnectedScreen() {
  const state = useMobileState()
  const session = useSession()
  const router = useRouter()
  const { t } = useI18n()
  const name = state.pairing.step === 'connected' ? state.pairing.name : (state.computer?.name ?? '')
  return (
    <Outcome
      mascot="celebrate"
      title={t('pair.connectedTitle', { computer: name })}
      note={t('pair.connectedNote', { computer: name })}
      actions={
        <PhoneButton
          onPress={() => {
            session.resetPairing()
            if (router.canDismiss()) router.dismissAll()
            router.replace('/')
          }}
        >
          {t('pair.continue')}
        </PhoneButton>
      }
    />
  )
}

export function PairInvalidScreen() {
  const session = useSession()
  const router = useRouter()
  const { t } = useI18n()
  return (
    <Outcome
      icon="qrCode"
      title={t('pair.invalidTitle')}
      note={t('pair.invalidNote')}
      actions={
        <PhoneButton
          onPress={() => {
            session.resetPairing()
            router.replace('/pair')
          }}
        >
          {t('pair.scanNew')}
        </PhoneButton>
      }
    />
  )
}

export function IdentityChangedScreen() {
  const state = useMobileState()
  const session = useSession()
  const { t } = useI18n()
  const name = state.computer?.name ?? ''
  return (
    <Outcome
      mascot="wary"
      title={t('identity.title', { computer: name })}
      note={t('identity.note', { computer: name })}
      actions={
        <PhoneButton variant="danger" onPress={() => void session.removeComputer()}>
          {t('identity.remove', { computer: name })}
        </PhoneButton>
      }
    />
  )
}

const styles = StyleSheet.create({
  scan: { alignItems: 'center' },
  white: { color: camera.text },
  centered: { textAlign: 'center' },
  revoked: { alignSelf: 'stretch', marginHorizontal: 24, marginBottom: 24 },
  scanMascot: { marginTop: 8 },
  scanCopy: { marginTop: 12, paddingHorizontal: 32, alignItems: 'center' },
  scanNote: { marginTop: 8, color: camera.textMuted },
  frame: { width: FRAME, height: FRAME, marginTop: 48 },
  viewfinder: { position: 'absolute', top: 6, left: 6, right: 6, bottom: 6, borderRadius: 24, overflow: 'hidden' },
  corner: { position: 'absolute', width: 40, height: 40, borderColor: camera.text },
  permission: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: 12, padding: 16 },
  permissionNote: { color: camera.textMuted, textAlign: 'center' },
  hint: { marginTop: 28, marginHorizontal: 32, color: camera.textHint },
  outcomeBody: { flex: 1, alignItems: 'center', justifyContent: 'center', gap: 10, paddingHorizontal: 32, paddingBottom: 48 },
  outcomeMascot: { marginBottom: 8 },
  badge: { width: 64, height: 64, marginBottom: 8, borderRadius: metrics.heroRadius, alignItems: 'center', justifyContent: 'center' },
  outcomeNote: { maxWidth: 300 },
  extra: { fontWeight: '500' },
  actions: { gap: 8, paddingHorizontal: metrics.gutter },
})
