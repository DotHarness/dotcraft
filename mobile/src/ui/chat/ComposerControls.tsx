import type { ModelCatalogItem } from '@dotcraft/sdk/contracts'
import { useEffect, useState, type ReactNode } from 'react'
import { Pressable, StyleSheet, Switch, Text, View } from 'react-native'
import { useSession } from '../../app-state/SessionContext'
import type { ProjectModels } from '../../core/state'
import type { ApprovalPolicy, ChatControls, ReasoningValue, Speed } from '../../core/threadConfig'
import { useI18n, type I18n } from '../../i18n'
import type { MessageId } from '../../i18n/messages/en'
import { Icon, type IconName } from '../icons'
import { PhoneButton, Spinner, Txt } from '../parts'
import { SheetHeader, SheetLayer } from '../Sheet'
import { type, useTheme } from '../theme'

export type ControlChange =
  | { kind: 'provider'; providerId: string }
  | { kind: 'model'; providerId: string | null; model: string }
  | { kind: 'reasoning'; value: ReasoningValue }
  | { kind: 'speed'; speed: Speed }
  | { kind: 'approval'; policy: ApprovalPolicy }

const EFFORT_LABEL: Record<string, MessageId> = {
  off: 'reasoning.off',
  low: 'reasoning.low',
  medium: 'reasoning.medium',
  high: 'reasoning.high',
  extraHigh: 'reasoning.extraHigh',
  max: 'reasoning.max',
  ultra: 'reasoning.ultra',
}

function effortLabel(t: I18n['t'], value: string, fallback?: string): string {
  const id = EFFORT_LABEL[value]
  return id ? t(id) : (fallback ?? value)
}

export function catalogItem(models: ProjectModels | undefined, providerId: string | null, model: string | null): ModelCatalogItem | undefined {
  if (!models || !providerId || !model) return undefined
  return models.catalogs[providerId]?.find((item) => item.id === model)
}

export function defaultModel(models: ProjectModels | undefined, providerId: string | null): string | null {
  if (!models || !providerId) return null
  return models.catalogs[providerId]?.find((item) => item.isDefault)?.id ?? null
}

function OptionRow({
  icon,
  label,
  description,
  selected,
  disabled,
  onPress,
}: {
  icon?: ReactNode
  label: string
  description?: string
  selected: boolean
  disabled?: boolean
  onPress: () => void
}) {
  const { colors } = useTheme()
  return (
    <Pressable
      accessibilityRole="radio"
      accessibilityState={{ checked: selected, disabled }}
      disabled={disabled}
      onPress={onPress}
      style={({ pressed }) => [styles.option, pressed && { backgroundColor: colors.bgTertiary }, disabled && styles.disabled]}
    >
      {icon ? <View style={styles.optionIcon}>{icon}</View> : null}
      <View style={styles.optionText}>
        <Txt numberOfLines={1} style={styles.optionLabel}>
          {label}
        </Txt>
        {description ? (
          <Txt variant="meta" tone="secondary">
            {description}
          </Txt>
        ) : null}
      </View>
      <View style={{ opacity: selected ? 1 : 0 }}>
        <Icon name="check" size={18} color={colors.textPrimary} strokeWidth={2} />
      </View>
    </Pressable>
  )
}

type Section = 'provider' | 'model' | 'reasoning'

function ExpandRow({
  label,
  value,
  expanded,
  onPress,
  children,
}: {
  label: string
  value: string
  expanded: boolean
  onPress: () => void
  children: ReactNode
}) {
  const { colors } = useTheme()
  return (
    <View>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={label}
        accessibilityValue={{ text: value }}
        accessibilityState={{ expanded }}
        onPress={onPress}
        style={({ pressed }) => [styles.option, pressed && { backgroundColor: colors.bgTertiary }]}
      >
        <Txt style={[styles.optionLabel, styles.rowLabel]}>{label}</Txt>
        <Txt numberOfLines={1} tone="secondary" style={styles.rowValue}>
          {value}
        </Txt>
        <View style={{ transform: [{ rotate: expanded ? '90deg' : '0deg' }] }}>
          <Icon name="chevronRight" size={16} color={colors.textDimmed} />
        </View>
      </Pressable>
      {expanded ? (
        <View accessibilityRole="radiogroup" style={[styles.choices, { borderLeftColor: colors.borderDefault }]}>
          {children}
        </View>
      ) : null}
    </View>
  )
}

function useApply(onChange: (change: ControlChange) => Promise<void>) {
  const [busy, setBusy] = useState(false)
  const [failed, setFailed] = useState(false)
  const apply = async (change: ControlChange) => {
    setBusy(true)
    setFailed(false)
    try {
      await onChange(change)
      return true
    } catch {
      setFailed(true)
      return false
    } finally {
      setBusy(false)
    }
  }
  return { busy, failed, apply }
}

const POLICY_ICON: Record<ApprovalPolicy, IconName> = { prompt: 'hand', autoApprove: 'triangleAlert' }

function ApprovalSheet({
  visible,
  policy,
  onClose,
  onChange,
}: {
  visible: boolean
  policy: ApprovalPolicy
  onClose: () => void
  onChange: (change: ControlChange) => Promise<void>
}) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const [confirming, setConfirming] = useState(false)
  const { busy, failed, apply } = useApply(onChange)
  const close = () => {
    setConfirming(false)
    onClose()
  }
  const choose = async (next: ApprovalPolicy) => {
    if (next === policy) return close()
    if (next === 'autoApprove' && !confirming) return setConfirming(true)
    if (await apply({ kind: 'approval', policy: next })) close()
  }
  if (confirming) {
    return (
      <SheetLayer visible={visible} onClose={close} alert>
        <SheetHeader title={t('approval.fullAccess.warningTitle')} onClose={close} />
        <Txt tone="secondary">{t('approval.fullAccess.warningBody')}</Txt>
        {failed ? <Txt tone="error">{t('controls.failed')}</Txt> : null}
        <View style={styles.decision}>
          <PhoneButton variant="danger" loading={busy} onPress={() => void choose('autoApprove')}>
            {t('approval.fullAccess.warningConfirm')}
          </PhoneButton>
          <PhoneButton variant="ghost" onPress={() => setConfirming(false)}>
            {t('common.cancel')}
          </PhoneButton>
        </View>
      </SheetLayer>
    )
  }
  return (
    <SheetLayer visible={visible} onClose={close}>
      <SheetHeader title={t('approval.policy')} onClose={close} />
      {failed ? <Txt tone="error">{t('controls.failed')}</Txt> : null}
      <View accessibilityRole="radiogroup" style={styles.options}>
        {(['prompt', 'autoApprove'] as const).map((option) => (
          <OptionRow
            key={option}
            icon={<Icon name={POLICY_ICON[option]} size={18} color={colors.textSecondary} />}
            label={t(option === 'prompt' ? 'approval.prompt.label' : 'approval.fullAccess.label')}
            description={t(option === 'prompt' ? 'approval.prompt.description' : 'approval.fullAccess.description')}
            selected={option === policy}
            disabled={busy}
            onPress={() => void choose(option)}
          />
        ))}
      </View>
    </SheetLayer>
  )
}

function ModelSheet({
  visible,
  projectId,
  controls,
  models,
  onClose,
  onChange,
}: {
  visible: boolean
  projectId: string
  controls: ChatControls
  models: ProjectModels
  onClose: () => void
  onChange: (change: ControlChange) => Promise<void>
}) {
  const { t } = useI18n()
  const session = useSession()
  const [picked, setPicked] = useState<string | null>(null)
  const [open, setOpen] = useState<Section | null>(null)
  const viewing = picked ?? controls.providerId
  const { busy, failed, apply } = useApply(onChange)
  const catalog = viewing ? models.catalogs[viewing] : undefined
  const current = catalogItem(models, controls.providerId, controls.model)
  const reasoning = current?.reasoning
  const fast = current?.speed?.supportedModes?.includes('fast') === true

  useEffect(() => {
    if (visible && viewing && !models.catalogs[viewing]) void session.loadModels(projectId, viewing).catch(() => undefined)
  }, [visible, viewing, models.catalogs, projectId, session])

  const close = () => {
    setPicked(null)
    setOpen(null)
    onClose()
  }
  const toggle = (section: Section) => setOpen((value) => (value === section ? null : section))
  const choose = async (change: ControlChange) => {
    if (await apply(change)) setOpen(null)
  }
  const ids = (catalog ?? []).flatMap((item) => (item.id ? [item.id] : []))
  const sameProvider = viewing === controls.providerId
  if (sameProvider && controls.model && catalog && !ids.includes(controls.model)) ids.unshift(controls.model)
  const efforts = [...(reasoning?.supportsDisable ? [{ effort: 'off', label: undefined }] : []), ...(reasoning?.supportedEfforts ?? [])]
  const effective = controls.reasoning === 'default' ? reasoning?.defaultEffort : controls.reasoning
  const provider = models.providers.find((entry) => entry.id === viewing)

  return (
    <SheetLayer visible={visible} onClose={close}>
      <SheetHeader title={t('model.title')} onClose={close} />
      {failed ? <Txt tone="error">{t('controls.failed')}</Txt> : null}
      <View style={styles.options}>
        {models.providers.length > 1 ? (
          <ExpandRow label={t('model.provider')} value={provider?.name ?? viewing ?? ''} expanded={open === 'provider'} onPress={() => toggle('provider')}>
            {models.providers.map((entry) => (
              <OptionRow
                key={entry.id}
                label={entry.name}
                description={entry.name === entry.id ? undefined : entry.id}
                selected={entry.id === viewing}
                onPress={() => {
                  setPicked(entry.id)
                  if (entry.id === controls.providerId) return setOpen(null)
                  setOpen('model')
                  void onChange({ kind: 'provider', providerId: entry.id })
                }}
              />
            ))}
          </ExpandRow>
        ) : null}
        <ExpandRow label={t('model.title')} value={sameProvider ? (controls.model ?? '') : ''} expanded={open === 'model'} onPress={() => toggle('model')}>
          {catalog ? (
            <>
              {ids.length === 0 ? <Txt tone="secondary">{t('model.none')}</Txt> : null}
              {ids.map((id) => (
                <OptionRow
                  key={id}
                  label={id}
                  selected={sameProvider && id === controls.model}
                  disabled={busy}
                  onPress={() => void choose({ kind: 'model', providerId: viewing, model: id })}
                />
              ))}
            </>
          ) : (
            <View style={styles.loading}>
              <Spinner size={18} />
            </View>
          )}
        </ExpandRow>
        {reasoning && efforts.length > 0 ? (
          <ExpandRow
            label={t('reasoning.heading')}
            value={effective ? effortLabel(t, effective) : ''}
            expanded={open === 'reasoning'}
            onPress={() => toggle('reasoning')}
          >
            {efforts.map((option) => {
              const value = option.effort ?? ''
              return (
                <OptionRow
                  key={value}
                  label={effortLabel(t, value, option.label)}
                  selected={value === effective}
                  disabled={busy}
                  onPress={() => void choose({ kind: 'reasoning', value })}
                />
              )
            })}
          </ExpandRow>
        ) : null}
        {fast ? <FastRow value={controls.speed === 'fast'} disabled={busy} onChange={(on) => void apply({ kind: 'speed', speed: on ? 'fast' : 'standard' })} /> : null}
      </View>
    </SheetLayer>
  )
}

function FastRow({ value, disabled, onChange }: { value: boolean; disabled: boolean; onChange: (on: boolean) => void }) {
  const { t } = useI18n()
  const { colors } = useTheme()
  return (
    <View style={styles.fast}>
      <Icon name="zap" size={18} color={colors.textSecondary} fill={value ? colors.textSecondary : 'none'} />
      <View style={styles.optionText}>
        <Txt style={styles.optionLabel}>{t('speed.fast')}</Txt>
      </View>
      <Switch
        accessibilityLabel={t('speed.fast')}
        value={value}
        disabled={disabled}
        onValueChange={onChange}
        trackColor={{ true: colors.accent, false: colors.sendDisabled }}
        thumbColor="#ffffff"
      />
    </View>
  )
}

export function ComposerControls({
  projectId,
  controls,
  models,
  onChange,
}: {
  projectId: string
  controls: ChatControls
  models: ProjectModels | undefined
  onChange: (change: ControlChange) => Promise<void>
}) {
  const { t } = useI18n()
  const { colors } = useTheme()
  const [sheet, setSheet] = useState<'approval' | 'model' | null>(null)
  if (!models?.canConfigure) return null
  const auto = controls.approvalPolicy === 'autoApprove'
  const policyLabel = t(auto ? 'approval.fullAccess.label' : 'approval.prompt.label')
  const current = catalogItem(models, controls.providerId, controls.model)
  const fast = controls.speed === 'fast' && current?.speed?.supportedModes?.includes('fast') === true
  const effort = controls.reasoning === 'default' ? current?.reasoning?.defaultEffort : controls.reasoning
  const modelLabel = controls.model ?? t('model.title')
  return (
    <>
      <Pressable
        accessibilityRole="button"
        accessibilityLabel={`${t('approval.policy')}: ${policyLabel}`}
        onPress={() => setSheet('approval')}
        hitSlop={4}
        style={({ pressed }) => [styles.round, pressed && { backgroundColor: colors.roundFill }]}
      >
        <Icon name={POLICY_ICON[controls.approvalPolicy]} size={20} color={auto ? colors.warning : colors.textSecondary} />
      </Pressable>
      {models.canListModels ? (
        <Pressable
          accessibilityRole="button"
          accessibilityLabel={controls.model ? `${t('model.title')}: ${controls.model}` : t('model.title')}
          onPress={() => setSheet('model')}
          style={({ pressed }) => [styles.chip, pressed && { backgroundColor: colors.roundFill }]}
        >
          {fast ? <Icon name="zap" size={14} color={colors.textPrimary} fill={colors.textPrimary} strokeWidth={1.5} /> : null}
          <Text numberOfLines={1} style={[type.body, styles.chipModel, { color: colors.textPrimary }]}>
            {modelLabel}
          </Text>
          {effort && current?.reasoning ? (
            <Text style={[type.body, { color: colors.textSecondary }]}>{effortLabel(t, effort)}</Text>
          ) : null}
        </Pressable>
      ) : null}
      <ApprovalSheet visible={sheet === 'approval'} policy={controls.approvalPolicy} onClose={() => setSheet(null)} onChange={onChange} />
      {models.canListModels ? (
        <ModelSheet
          visible={sheet === 'model'}
          projectId={projectId}
          controls={controls}
          models={models}
          onClose={() => setSheet(null)}
          onChange={onChange}
        />
      ) : null}
    </>
  )
}

const styles = StyleSheet.create({
  round: { width: 36, height: 36, borderRadius: 18, alignItems: 'center', justifyContent: 'center' },
  chip: {
    marginLeft: 'auto',
    flexShrink: 1,
    minWidth: 0,
    flexDirection: 'row',
    alignItems: 'center',
    gap: 6,
    height: 36,
    paddingHorizontal: 10,
    borderRadius: 18,
  },
  chipModel: { flexShrink: 1, fontWeight: '500' },
  options: { marginHorizontal: -8 },
  option: { flexDirection: 'row', alignItems: 'center', gap: 12, minHeight: 48, paddingVertical: 8, paddingHorizontal: 8, borderRadius: 10 },
  optionIcon: { width: 24, alignItems: 'center' },
  optionText: { flex: 1, minWidth: 0, gap: 1 },
  optionLabel: { fontWeight: '500' },
  loading: { paddingVertical: 14, alignItems: 'center' },
  rowLabel: { flexShrink: 0 },
  rowValue: { flex: 1, minWidth: 0, textAlign: 'right' },
  choices: { marginLeft: 8, paddingLeft: 4, borderLeftWidth: 1 },
  fast: { flexDirection: 'row', alignItems: 'center', gap: 12, minHeight: 48, paddingHorizontal: 8 },
  decision: { gap: 8, marginTop: 4 },
  disabled: { opacity: 0.45 },
})
