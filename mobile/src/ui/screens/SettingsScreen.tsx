import { useRouter } from 'expo-router'
import { useState } from 'react'
import { StyleSheet, View } from 'react-native'
import { useMobileState, useSession } from '../../app-state/SessionContext'
import { appBuild, appVersion } from '../../platform/device'
import { useI18n } from '../../i18n'
import { Icon } from '../icons'
import { Hero, Screen, ScrollArea, TopBar } from '../layout'
import { BackButton, PhoneButton, RowChevron, Section, Txt } from '../parts'
import { Row } from '../rows'
import { ConfirmSheet } from '../Sheet'
import { type, useTheme } from '../theme'

export function PairDifferentSheet({ computer, visible, onClose }: { computer: string; visible: boolean; onClose: () => void }) {
  const session = useSession()
  const router = useRouter()
  const { t } = useI18n()
  return (
    <ConfirmSheet
      visible={visible}
      title={t('replace.title')}
      text={t('replace.text', { computer })}
      confirmLabel={t('replace.continue')}
      danger={false}
      onCancel={onClose}
      onConfirm={() => {
        onClose()
        session.resetPairing()
        router.push('/pair')
      }}
    />
  )
}

export function SettingsScreen() {
  const state = useMobileState()
  const session = useSession()
  const router = useRouter()
  const { t, date } = useI18n()
  const { colors } = useTheme()
  const [sheet, setSheet] = useState<'remove' | 'replace' | null>(null)
  const computer = state.computer
  if (!computer) return <Screen>{null}</Screen>

  const meta = [computer.version ? t('settings.computerVersion', { version: computer.version }) : null, t('settings.paired', { date: date(computer.pairedAt) })]
    .filter(Boolean)
    .join(' · ')
  const version = appBuild ? t('settings.appVersionBuild', { version: appVersion, build: appBuild }) : t('settings.appVersion', { version: appVersion })

  return (
    <Screen>
      <TopBar>
        <BackButton onPress={() => router.back()} />
      </TopBar>
      <ScrollArea last>
        <Hero>
          <Txt accessibilityRole="header" style={type.title}>
            {t('settings.title')}
          </Txt>
        </Hero>
        <Section title={t('settings.computer')}>
          <Row
            lead={<Icon name="monitor" size={20} color={colors.textSecondary} strokeWidth={1.7} />}
            title={computer.name}
            meta={meta}
            trail={
              <PhoneButton variant="danger" compact onPress={() => setSheet('remove')}>
                {t('settings.remove')}
              </PhoneButton>
            }
          />
          <Row
            single
            onPress={() => setSheet('replace')}
            lead={<Icon name="arrowLeftRight" size={20} color={colors.textSecondary} strokeWidth={1.7} />}
            title={t('settings.pairDifferent')}
            trail={<RowChevron />}
          />
        </Section>
        <Section title={t('settings.about')}>
          <View style={styles.versionRow}>
            <Txt style={styles.grow}>{t('settings.version')}</Txt>
            <Txt tone="secondary">{version}</Txt>
          </View>
        </Section>
      </ScrollArea>
      <ConfirmSheet
        visible={sheet === 'remove'}
        title={t('remove.title', { computer: computer.name })}
        text={t('remove.text', { computer: computer.name })}
        confirmLabel={t('remove.confirm')}
        danger
        onCancel={() => setSheet(null)}
        onConfirm={() => {
          setSheet(null)
          void session.removeComputer()
        }}
      />
      <PairDifferentSheet computer={computer.name} visible={sheet === 'replace'} onClose={() => setSheet(null)} />
    </Screen>
  )
}

const styles = StyleSheet.create({
  versionRow: { flexDirection: 'row', alignItems: 'center', gap: 12, minHeight: 48, paddingVertical: 8 },
  grow: { flex: 1 },
})
