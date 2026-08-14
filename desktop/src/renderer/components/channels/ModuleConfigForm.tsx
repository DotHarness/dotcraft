import { useMemo, useState } from 'react'
import type {
  ConfigFieldOptionWire,
  ConfigGroupDescriptorWire,
  DiscoveredModule,
  ModuleStatusEntry
} from '../../../preload/api.d'
import type { AppLocale } from '../../../shared/locales'
import { useLocale, useT } from '../../contexts/LocaleContext'
import type { ChannelConnectionState } from './ChannelCard'
import { FieldCard, SecretInput, StatusPill, formStyles } from './FormShared'
import { Input, Textarea } from '../ui/Input'
import { FolderIcon } from '../ui/AppIcons'
import { IconButton } from '../ui/IconButton'
import { Button } from '../ui/Button'
import { PillSwitch } from '../ui/PillSwitch'
import { Select } from '../ui/Select'
import { SettingsGroup, SettingsRow } from '../settings/SettingsGroup'
import { SETTINGS_SURFACE_CLASS } from '../settings/settingsTypography'
import styles from './ModuleConfigForm.module.css'

interface ModuleConfigFormProps {
  module: DiscoveredModule
  variantModules?: DiscoveredModule[]
  onVariantChange?: (moduleId: string) => void
  variantSwitching?: boolean
  config: Record<string, unknown>
  onChange: (next: Record<string, unknown>) => void
  onSave: () => void
  saving: boolean
  logoPath?: string
  moduleStatus?: ModuleStatusEntry
  persistedEnabled: boolean
  localControlsAvailable?: boolean
  onStart: () => void
  onCancel?: () => void
  qrDataUrl: string | null
  qrPhase: 'idle' | 'waitingForQr' | 'qrAvailable' | 'loginSuccess' | 'error'
  moduleLogLines: string[]
  logsLoading: boolean
  onLoadLogs: () => void
  hideHeader?: boolean
}

const CUSTOM_ENUM_VALUE = '__dotcraft_custom_value__'

function toText(value: unknown): string {
  if (value == null) return ''
  if (typeof value === 'string') return value
  if (typeof value === 'number' || typeof value === 'boolean') return String(value)
  try {
    return JSON.stringify(value)
  } catch {
    return ''
  }
}

function getNestedValue(obj: Record<string, unknown>, dottedKey: string): unknown {
  const parts = dottedKey.split('.').filter(Boolean)
  if (parts.length === 0) return undefined
  let current: unknown = obj
  for (const part of parts) {
    if (current == null || typeof current !== 'object' || Array.isArray(current)) {
      return undefined
    }
    current = (current as Record<string, unknown>)[part]
  }
  return current
}

function setNestedValue(
  obj: Record<string, unknown>,
  dottedKey: string,
  value: unknown
): Record<string, unknown> {
  const parts = dottedKey.split('.').filter(Boolean)
  if (parts.length === 0) return obj
  const result: Record<string, unknown> = { ...obj }
  let current: Record<string, unknown> = result
  for (let i = 0; i < parts.length - 1; i += 1) {
    const existing = current[parts[i]]
    const next =
      existing != null && typeof existing === 'object' && !Array.isArray(existing)
        ? { ...(existing as Record<string, unknown>) }
        : {}
    current[parts[i]] = next
    current = next
  }
  current[parts[parts.length - 1]] = value
  return result
}

function applyValueChange(
  config: Record<string, unknown>,
  key: string,
  value: unknown
): Record<string, unknown> {
  return setNestedValue(config, key, value)
}

function resolveModulePill(
  status: ModuleStatusEntry | undefined,
  persistedEnabled: boolean,
  t: (key: string) => string
): {
  status: ChannelConnectionState
  label: string
} {
  if (!status) {
    if (persistedEnabled) {
      return { status: 'stopped', label: t('channels.modules.stopped') }
    }
    return { status: 'notConfigured', label: t('channels.status.notConfigured') }
  }
  if (status.processState === 'crashed') {
    return { status: 'error', label: t('channels.modules.error') }
  }
  if (status.connected) {
    return { status: 'connected', label: t('channels.status.connected') }
  }
  if (status.processState === 'starting') {
    return { status: 'connecting', label: t('channels.modules.connecting') }
  }
  if (status.processState === 'running') {
    return { status: 'enabledNotConnected', label: t('channels.status.enabledNotConnected') }
  }
  if (status.processState === 'stopped') {
    if (persistedEnabled) {
      return { status: 'stopped', label: t('channels.modules.stopped') }
    }
    return { status: 'notConfigured', label: t('channels.status.notConfigured') }
  }
  return { status: 'notConfigured', label: t('channels.status.notConfigured') }
}

function resolveModuleDisplayName(
  module: Pick<DiscoveredModule, 'displayName' | 'localizedDisplayName'>,
  locale: AppLocale
): string {
  return module.localizedDisplayName?.[locale] ?? module.displayName
}

export function ModuleConfigForm({
  module,
  variantModules = [],
  onVariantChange,
  variantSwitching = false,
  config,
  onChange,
  onSave,
  saving,
  logoPath,
  moduleStatus,
  persistedEnabled,
  localControlsAvailable = true,
  onStart,
  onCancel,
  qrDataUrl,
  qrPhase,
  moduleLogLines,
  logsLoading,
  onLoadLogs,
  hideHeader = false
}: ModuleConfigFormProps): JSX.Element {
  const locale = useLocale()
  const t = useT()
  const [listTextByKey, setListTextByKey] = useState<Record<string, string>>({})
  const [objectTextByKey, setObjectTextByKey] = useState<Record<string, string>>({})
  const [customEnumByKey, setCustomEnumByKey] = useState<Record<string, boolean>>({})
  const descriptors = useMemo(
    () =>
      module.configDescriptors.filter(
        (descriptor) =>
          descriptor.interactiveSetupOnly !== true && !descriptor.key.startsWith('dotcraft.')
      ),
    [module.configDescriptors]
  )
  const descriptorGroups = useMemo(() => {
    const configuredGroups = module.configGroups ?? []
    const configuredIds = new Set(configuredGroups.map((group) => group.id))
    const grouped = new Map<string, typeof descriptors>()
    const resolveGroupId = (descriptor: (typeof descriptors)[number]): string => {
      if (descriptor.group) return descriptor.group
      if (descriptor.advanced === true) {
        return configuredIds.has('advanced') ? 'advanced' : '__legacy_advanced'
      }
      return configuredIds.has('configuration') ? 'configuration' : '__legacy_configuration'
    }
    for (const descriptor of descriptors) {
      const groupId = resolveGroupId(descriptor)
      grouped.set(groupId, [...(grouped.get(groupId) ?? []), descriptor])
    }
    const explicit = configuredGroups
      .map((group) => ({ group, descriptors: grouped.get(group.id) ?? [] }))
      .filter((entry) => entry.descriptors.length > 0)
    const legacy = [
      {
        group: { id: '__legacy_configuration', displayLabel: t('channels.detail.configuration') },
        descriptors: grouped.get('__legacy_configuration') ?? []
      },
      {
        group: { id: '__legacy_advanced', displayLabel: t('channels.modules.advancedGroup') },
        descriptors: grouped.get('__legacy_advanced') ?? []
      }
    ].filter((entry) => entry.descriptors.length > 0)
    return [...explicit, ...legacy]
  }, [descriptors, module.configGroups, t])
  const pill = resolveModulePill(moduleStatus, persistedEnabled, t)
  const showQrPanel = module.requiresInteractiveSetup && qrPhase !== 'idle'
  const hasVariants = variantModules.length > 1
  const moduleDisplayName = resolveModuleDisplayName(module, locale)

  const resolveDescriptorLabel = (
    descriptor: DiscoveredModule['configDescriptors'][number]
  ): string => descriptor.localizedDisplayLabel?.[locale] ?? descriptor.displayLabel

  const resolveDescriptorDescription = (
    descriptor: DiscoveredModule['configDescriptors'][number]
  ): string => descriptor.localizedDescription?.[locale] ?? descriptor.description

  const resolveGroupLabel = (group: ConfigGroupDescriptorWire): string =>
    group.localizedDisplayLabel?.[locale] ?? group.displayLabel

  const renderDescriptorField = (descriptor: DiscoveredModule['configDescriptors'][number]): JSX.Element => {
    const configuredValue = getNestedValue(config, descriptor.key)
    const value = configuredValue === undefined ? descriptor.defaultValue : configuredValue
    const displayLabel = resolveDescriptorLabel(descriptor)
    const description = resolveDescriptorDescription(descriptor)
    const requiredSuffix = descriptor.required ? ` (${t('channels.modules.required')})` : ''
    const placeholder = descriptor.defaultValue === undefined ? undefined : String(descriptor.defaultValue)

    if (descriptor.dataKind === 'boolean') {
      return (
        <SettingsRow
          key={descriptor.key}
          label={`${displayLabel}${requiredSuffix}`}
          description={description}
          control={(
            <PillSwitch
            checked={value === true || (value === undefined && descriptor.defaultValue === true)}
            aria-label={displayLabel}
            onChange={(checked) => {
              onChange(applyValueChange(config, descriptor.key, checked))
            }}
            />
          )}
        />
      )
    }

    if (descriptor.dataKind === 'enum') {
      const descriptorOptions: ConfigFieldOptionWire[] = descriptor.options ?? (descriptor.enumValues ?? []).map((item) => ({
        value: item,
        displayLabel: item
      }))
      const stringValue = typeof value === 'string' ? value : ''
      const isKnownValue = descriptorOptions.some((option) => option.value === stringValue)
      const customActive = descriptor.allowCustomValue === true &&
        (customEnumByKey[descriptor.key] === true || (stringValue !== '' && !isKnownValue))
      const selectOptions = descriptorOptions.map((option) => {
        const label = option.localizedDisplayLabel?.[locale] ?? option.displayLabel
        return {
          value: option.value,
          label: option.preview ? `${option.preview}  ${label} · ${option.value}` : label,
          description: option.localizedDescription?.[locale] ?? option.description
        }
      })
      if (descriptor.allowCustomValue === true) {
        selectOptions.push({ value: CUSTOM_ENUM_VALUE, label: t('channels.modules.customValue'), description: undefined })
      }
      if (stringValue === '' && !customActive) {
        selectOptions.unshift({ value: '', label: '', description: undefined })
      }
      return (
        <SettingsRow
          key={descriptor.key}
          orientation="block"
          label={`${displayLabel}${requiredSuffix}`}
          description={description}
          control={(
            <div className={styles.fullWidthControl}>
              <Select
                value={customActive ? CUSTOM_ENUM_VALUE : stringValue}
                onValueChange={(nextValue) => {
                  if (nextValue === CUSTOM_ENUM_VALUE) {
                    setCustomEnumByKey((current) => ({ ...current, [descriptor.key]: true }))
                    return
                  }
                  setCustomEnumByKey((current) => ({ ...current, [descriptor.key]: false }))
                  onChange(applyValueChange(config, descriptor.key, nextValue))
                }}
                ariaLabel={displayLabel}
                adaptiveWidth={false}
                style={{ width: '100%' }}
                options={selectOptions}
              />
              {customActive && (
                <Input
                  className={styles.customValueInput}
                  value={isKnownValue ? '' : stringValue}
                  placeholder={t('channels.modules.customValuePlaceholder')}
                  aria-label={`${displayLabel} ${t('channels.modules.customValue')}`}
                  mono
                  onChange={(event) => {
                    const next = event.target.value
                    onChange(applyValueChange(config, descriptor.key, next === '' ? undefined : next))
                  }}
                />
              )}
            </div>
          )}
        />
      )
    }

    if (descriptor.dataKind === 'list') {
      const textValue =
        listTextByKey[descriptor.key] ??
        (Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string').join('\n') : '')
      return (
        <SettingsRow
          key={descriptor.key}
          orientation="block"
          label={`${displayLabel}${requiredSuffix}`}
          description={description}
          control={(
            <Textarea
              value={textValue}
              placeholder={placeholder}
              onChange={(event) => {
                const nextText = event.target.value
                setListTextByKey((prev) => ({ ...prev, [descriptor.key]: nextText }))
                const nextList = nextText
                  .split('\n')
                  .map((item) => item.trim())
                  .filter(Boolean)
                onChange(applyValueChange(config, descriptor.key, nextList))
              }}
              style={{ minHeight: '90px', height: 'auto', padding: '8px 10px' }}
            />
          )}
        />
      )
    }

    if (descriptor.dataKind === 'object') {
      const textValue = objectTextByKey[descriptor.key] ?? (value == null ? '' : JSON.stringify(value, null, 2))
      return (
        <SettingsRow
          key={descriptor.key}
          orientation="block"
          label={`${displayLabel}${requiredSuffix}`}
          description={description}
          control={(
            <Textarea
              value={textValue}
              placeholder={placeholder}
              onChange={(event) => {
                setObjectTextByKey((prev) => ({ ...prev, [descriptor.key]: event.target.value }))
              }}
              onBlur={(event) => {
                const raw = event.target.value.trim()
                if (raw === '') {
                  onChange(applyValueChange(config, descriptor.key, undefined))
                  return
                }
                try {
                  const parsed = JSON.parse(raw) as unknown
                  onChange(applyValueChange(config, descriptor.key, parsed))
                } catch {
                  // Keep user text untouched until it is valid JSON.
                }
              }}
              style={{ minHeight: '120px', height: 'auto', padding: '8px 10px' }}
            />
          )}
        />
      )
    }

    if (descriptor.dataKind === 'number') {
      return (
        <SettingsRow
          key={descriptor.key}
          orientation="block"
          label={`${displayLabel}${requiredSuffix}`}
          description={description}
          control={(
            <Input
              type="number"
              className="dc-plain-number"
              value={typeof value === 'number' && Number.isFinite(value) ? String(value) : ''}
              placeholder={placeholder}
              onChange={(event) => {
                const nextRaw = event.target.value.trim()
                const parsed = nextRaw === '' ? undefined : Number.parseFloat(nextRaw)
                onChange(
                  applyValueChange(
                    config,
                    descriptor.key,
                    parsed === undefined || Number.isNaN(parsed) ? undefined : parsed
                  )
                )
              }}
            />
          )}
        />
      )
    }

    if (descriptor.dataKind === 'path') {
      return (
        <SettingsRow
          key={descriptor.key}
          orientation="block"
          label={`${displayLabel}${requiredSuffix}`}
          description={description}
          control={(
            <div className={styles.pathControl}>
              <Input
                value={toText(value)}
                placeholder={placeholder}
                onChange={(event) => {
                  onChange(applyValueChange(config, descriptor.key, event.target.value))
                }}
                style={{ flex: 1 }}
              />
              <IconButton
                icon={<FolderIcon size={16} />}
                label={t('settings.modulesDirectoryBrowse')}
                onClick={() => {
                  void window.api.modules.pickDirectory().then((pickedPath) => {
                    if (!pickedPath) return
                    onChange(applyValueChange(config, descriptor.key, pickedPath))
                  })
                }}
              />
            </div>
          )}
        />
      )
    }

    const isSecret = descriptor.dataKind === 'secret' || descriptor.masked
    return (
      <SettingsRow
        key={descriptor.key}
        orientation="block"
        label={`${displayLabel}${requiredSuffix}`}
        description={description}
        control={isSecret ? (
            <SecretInput
              value={toText(value)}
              placeholder={placeholder}
              onChange={(nextValue) => {
                onChange(applyValueChange(config, descriptor.key, nextValue))
              }}
            />
          ) : (
            <Input
              value={toText(value)}
              placeholder={placeholder}
              onChange={(event) => {
                onChange(applyValueChange(config, descriptor.key, event.target.value))
              }}
            />
          )}
      />
    )
  }

  return (
    <div className={`${styles.form} ${SETTINGS_SURFACE_CLASS}`}>
      {!hideHeader && (
        <div style={formStyles.header}>
          {logoPath ? (
            <img
              src={logoPath}
              alt={moduleDisplayName}
              width={44}
              height={44}
              style={formStyles.headerLogo}
            />
          ) : (
            <div
              aria-hidden
              style={{
                ...formStyles.headerLogo,
                width: 44,
                height: 44,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                color: 'var(--text-secondary)',
                fontSize: '18px',
                fontWeight: 700
              }}
            >
              {moduleDisplayName.slice(0, 1).toUpperCase()}
            </div>
          )}

          <div style={{ minWidth: 0 }}>
            <div style={formStyles.headerTitle}>{moduleDisplayName}</div>
            <div
              style={{
                marginTop: '4px',
                display: 'flex',
                alignItems: 'center',
                gap: '8px'
              }}
            >
              <span
                style={{
                  fontSize: '11px',
                  color: 'var(--text-dimmed)',
                  border: '1px solid var(--border-default)',
                  borderRadius: '999px',
                  padding: '2px 8px'
                }}
              >
                {module.source === 'bundled'
                  ? t('channels.modules.source.bundled')
                  : t('channels.modules.source.user')}
              </span>
              <StatusPill status={pill.status} label={pill.label} />
            </div>
          </div>
        </div>
      )}

      {hasVariants && (
        <FieldCard>
          <div style={formStyles.fieldGroup}>
            <label style={formStyles.label}>{t('channels.modules.variant.active')}</label>
            <Select
              value={module.moduleId}
              disabled={variantSwitching}
              onValueChange={(moduleId) => {
                onVariantChange?.(moduleId)
              }}
              ariaLabel={t('channels.modules.variant.active')}
              options={variantModules.map((variant) => ({
                value: variant.moduleId,
                label: t('channels.modules.variant.option', {
                  name: resolveModuleDisplayName(variant, locale),
                  variant: variant.variant
                })
              }))}
            />
          </div>
        </FieldCard>
      )}

      {moduleStatus?.processState === 'crashed' && (
        <div
          style={{
            marginBottom: '12px',
            border: '1px solid rgba(255, 69, 58, 0.45)',
            backgroundColor: 'rgba(255, 69, 58, 0.12)',
            borderRadius: '8px',
            padding: '10px 12px',
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'stretch',
            gap: '8px'
          }}
        >
          <span style={{ fontSize: '12px', color: 'var(--error, #ff453a)' }}>
            {t('channels.modules.crashBanner')}
          </span>
          {moduleStatus.failureCode === 'externalChannelStartFailed' && (
            <span style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>
              {t('channels.modules.failure.externalChannelStartFailed')}
            </span>
          )}
          <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end' }}>
            <Button
              size="sm"
              variant="secondary"
              onClick={onLoadLogs}
              loading={logsLoading}
            >
              {logsLoading ? t('channels.modules.logs.loading') : t('channels.modules.logs.view')}
            </Button>
            {localControlsAvailable && (
              <Button
                size="sm"
                variant="primary"
                onClick={onStart}
              >
                {t('channels.modules.restart')}
              </Button>
            )}
          </div>
        </div>
      )}

      {moduleLogLines.length > 0 && (
        <FieldCard>
          <div style={{ fontSize: '12px', color: 'var(--text-secondary)', marginBottom: '8px' }}>
            {t('channels.modules.logs.title')}
          </div>
          <pre
            style={{
              margin: 0,
              padding: '8px',
              borderRadius: '6px',
              backgroundColor: 'var(--bg-secondary)',
              color: 'var(--text-secondary)',
              fontSize: '11px',
              lineHeight: 1.4,
              whiteSpace: 'pre-wrap',
              maxHeight: '280px',
              overflow: 'auto'
            }}
          >
            {moduleLogLines.join('\n')}
          </pre>
        </FieldCard>
      )}

      {showQrPanel && (
        <FieldCard>
          <div
            style={{
              display: 'flex',
              flexDirection: 'column',
              gap: '10px',
              alignItems: 'center',
              textAlign: 'center'
            }}
          >
            {qrPhase === 'waitingForQr' && (
              <>
                <div
                  style={{
                    width: 200,
                    height: 200,
                    borderRadius: 12,
                    border: '1px dashed var(--border-default)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    color: 'var(--text-secondary)',
                    fontSize: 14
                  }}
                >
                  {t('channels.modules.qr.refreshing')}
                </div>
                <div style={{ fontSize: 13, color: 'var(--text-secondary)' }}>
                  {t('channels.modules.qr.waitingForQr')}
                </div>
              </>
            )}

            {qrPhase === 'qrAvailable' && (
              <>
                <div
                  style={{
                    width: 220,
                    height: 220,
                    borderRadius: 12,
                    border: '1px solid var(--border-default)',
                    backgroundColor: 'var(--bg-secondary)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    overflow: 'hidden'
                  }}
                >
                  {qrDataUrl ? (
                    <img
                      src={qrDataUrl}
                      alt="Weixin QR"
                      width={200}
                      height={200}
                      style={{ width: 200, height: 200, display: 'block' }}
                    />
                  ) : (
                    <div style={{ fontSize: 12, color: 'var(--text-dimmed)' }}>
                      {t('channels.modules.qr.waitingForQr')}
                    </div>
                  )}
                </div>
                <div style={{ fontSize: 14, fontWeight: 600, color: 'var(--text-primary)' }}>
                  {t('channels.modules.qr.scanPrompt')}
                </div>
                <div style={{ fontSize: 12, color: 'var(--text-secondary)' }}>
                  {t('channels.modules.qr.waitingForScan')}
                </div>
              </>
            )}

            {qrPhase === 'loginSuccess' && (
              <>
                <div style={{ fontSize: 36, lineHeight: 1, color: 'var(--success, #34c759)' }}>✓</div>
                <div style={{ fontSize: 14, fontWeight: 600, color: 'var(--success, #34c759)' }}>
                  {t('channels.modules.qr.loginSuccess')}
                </div>
              </>
            )}

            {qrPhase === 'error' && (
              <>
                <div style={{ fontSize: 28, lineHeight: 1, color: 'var(--error, #ff453a)' }}>!</div>
                <div style={{ fontSize: 14, fontWeight: 600, color: 'var(--error, #ff453a)' }}>
                  {t('channels.modules.qr.error')}
                </div>
                <Button
                  size="sm"
                  variant="primary"
                  onClick={onStart}
                >
                  {t('channels.modules.qr.retry')}
                </Button>
              </>
            )}
          </div>
        </FieldCard>
      )}

      <div className={styles.configurationGroups}>
        {descriptorGroups.map(({ group, descriptors: groupDescriptors }) => (
          <SettingsGroup key={group.id} title={resolveGroupLabel(group)}>
            {groupDescriptors.map((descriptor) => renderDescriptorField(descriptor))}
          </SettingsGroup>
        ))}
      </div>

      <div className={styles.actions}>
        {onCancel && (
          <Button variant="secondary" onClick={onCancel}>
            {t('common.cancel')}
          </Button>
        )}
        <Button variant="primary" onClick={onSave} loading={saving}>
          {saving ? t('channels.saving') : t('channels.save')}
        </Button>
      </div>
    </div>
  )
}
