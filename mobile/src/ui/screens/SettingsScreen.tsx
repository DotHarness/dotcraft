import { useRouter } from 'expo-router'
import { useState } from 'react'
import { StyleSheet, View } from 'react-native'
import { useMobileState, useSession } from '../../app-state/SessionContext'
import { computerStatus, type ComputerState } from '../../core/state'
import { appBuild, appVersion } from '../../platform/device'
import { useI18n } from '../../i18n'
import { Icon } from '../icons'
import { Hero, Screen, ScrollArea, TopBar } from '../layout'
import { BackButton, PhoneButton, RowChevron, Section, Txt, useStatusLabel } from '../parts'
import { Row } from '../rows'
import { ConfirmSheet } from '../Sheet'
import { type, useTheme } from '../theme'

export function useAddComputer(): () => void {
  const session = useSession()
  const router = useRouter()
  return () => {
    session.resetPairing()
    router.push('/pair')
  }
}

function ComputerRow({ computer, onRemove }: { computer: ComputerState; onRemove: () => void }) {
  const { t, date } = useI18n()
  const { colors } = useTheme()
  const statusLabel = useStatusLabel()
  const record = computer.computer
  const meta = [
    statusLabel(computerStatus(computer)),
    record.version ? t('settings.computerVersion', { version: record.version }) : null,
    t('settings.paired', { date: date(record.pairedAt) }),
  ]
    .filter(Boolean)
    .join(' · ')
  return (
    <Row
      lead={<Icon name="monitor" size={20} color={colors.textSecondary} strokeWidth={1.7} />}
      title={record.name}
      meta={meta}
      trail={
        <PhoneButton variant="danger" compact onPress={onRemove}>
          {t('settings.remove')}
        </PhoneButton>
      }
    />
  )
}

export function SettingsScreen() {
  const state = useMobileState()
  const session = useSession()
  const router = useRouter()
  const { t } = useI18n()
  const { colors } = useTheme()
  const addComputer = useAddComputer()
  const [removing, setRemoving] = useState<string | null>(null)
  const target = removing ? state.computers[removing]?.computer : undefined
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
        <Section title={t('settings.computers')}>
          {state.order.map((id) => (
            <ComputerRow key={id} computer={state.computers[id]} onRemove={() => setRemoving(id)} />
          ))}
          <Row
            single
            onPress={addComputer}
            lead={<Icon name="plus" size={20} color={colors.textSecondary} strokeWidth={1.7} />}
            title={t('settings.addComputer')}
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
        visible={Boolean(target)}
        title={t('remove.title', { computer: target?.name ?? '' })}
        text={t('remove.text', { computer: target?.name ?? '' })}
        confirmLabel={t('remove.confirm')}
        danger
        onCancel={() => setRemoving(null)}
        onConfirm={() => {
          setRemoving(null)
          if (removing) void session.removeComputer(removing)
        }}
      />
    </Screen>
  )
}

const styles = StyleSheet.create({
  versionRow: { flexDirection: 'row', alignItems: 'center', gap: 12, minHeight: 48, paddingVertical: 8 },
  grow: { flex: 1 },
})
