import { useMemo, useState } from 'react'
import type { ConfigDescriptorWire, ConfigFieldOptionWire, ConfigGroupDescriptorWire, ModuleStatusEntry } from '../../../shared/channelModules'
import { channelTitle, type ChannelPlugin } from './channelPluginModel'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { FieldCard } from './FormShared'
import { ChannelSecretInput } from './ChannelSecretInput'
import { Input, Textarea } from '../ui/Input'
import { Button } from '../ui/Button'
import { PillSwitch } from '../ui/PillSwitch'
import { Select } from '../ui/Select'
import { SettingsGroup, SettingsRow } from '../settings/SettingsGroup'
import { SettingsDescriptionWithLearnMore } from '../settings/SettingsLearnMoreLink'
import { SETTINGS_DOCS_BASE_URL } from '../settings/settingsDocs'
import { SETTINGS_SURFACE_CLASS } from '../settings/settingsTypography'
import { ListOptionsField } from './ListOptionsField'
import styles from './ModuleConfigForm.module.css'

interface ModuleConfigFormProps {
  plugin: ChannelPlugin
  config: Record<string, unknown>
  configuredSecrets: readonly string[]
  onChange: (next: Record<string, unknown>) => void
  onSave: () => void
  saving: boolean
  readOnly: boolean
  moduleStatus?: ModuleStatusEntry
  onStart?: () => void
  onCancel: () => void
  moduleLogLines: string[] | null
  logsLoading: boolean
  onLoadLogs?: () => void
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

function applyValueChange(config: Record<string, unknown>, key: string, value: unknown): Record<string, unknown> {
  return { ...config, [key]: value }
}

export function ModuleConfigForm({
  plugin,
  config,
  configuredSecrets,
  onChange,
  onSave,
  saving,
  readOnly,
  moduleStatus,
  onStart,
  onCancel,
  moduleLogLines,
  logsLoading,
  onLoadLogs
}: ModuleConfigFormProps): JSX.Element {
  const module = plugin.channel
  const locale = useLocale()
  const t = useT()
  const [listTextByKey, setListTextByKey] = useState<Record<string, string>>({})
  const [objectTextByKey, setObjectTextByKey] = useState<Record<string, string>>({})
  const [customEnumByKey, setCustomEnumByKey] = useState<Record<string, boolean>>({})
  const [invalidSecrets, setInvalidSecrets] = useState<ReadonlySet<string>>(() => new Set())
  const descriptors = useMemo(
    () =>
      (module.configDescriptors ?? []).filter(
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
  const moduleDisplayName = channelTitle(plugin, locale)

  const resolveDescriptorLabel = (
    descriptor: ConfigDescriptorWire
  ): string => descriptor.localizedDisplayLabel?.[locale] ?? descriptor.displayLabel

  const resolveDescriptorDescription = (
    descriptor: ConfigDescriptorWire
  ): string => descriptor.localizedDescription?.[locale] ?? descriptor.description

  const resolveDescriptorDocsUrl = (
    descriptor: ConfigDescriptorWire
  ): string | undefined => {
    const route = descriptor.docsPath?.[locale] ?? descriptor.docsPath?.en
    return route ? `${SETTINGS_DOCS_BASE_URL}${locale === 'zh-Hans' ? '/zh' : ''}${route}` : undefined
  }

  const resolveGroupLabel = (group: ConfigGroupDescriptorWire): string =>
    group.localizedDisplayLabel?.[locale] ?? group.displayLabel

  const renderDescriptorField = (descriptor: ConfigDescriptorWire): JSX.Element => {
    const configuredValue = Object.hasOwn(config, descriptor.key) ? config[descriptor.key] : undefined
    const value = configuredValue === undefined ? descriptor.defaultValue : configuredValue
    const displayLabel = resolveDescriptorLabel(descriptor)
    const descriptionText = resolveDescriptorDescription(descriptor)
    const docsHref = resolveDescriptorDocsUrl(descriptor)
    const description = docsHref === undefined ? descriptionText : (
      <SettingsDescriptionWithLearnMore href={docsHref} aboutKey={displayLabel}>
        {descriptionText}
      </SettingsDescriptionWithLearnMore>
    )
    const requiredSuffix = descriptor.required ? ` (${t('channels.modules.required')})` : ''
    const placeholder = descriptor.defaultValue === undefined ? undefined : String(descriptor.defaultValue)

    if (descriptor.masked || descriptor.dataKind === 'secret') {
      return (
        <SettingsRow
          key={descriptor.key}
          orientation="block"
          label={`${displayLabel}${requiredSuffix}`}
          description={description}
          control={(
            <ChannelSecretInput
              label={displayLabel}
              kind={descriptor.dataKind}
              configured={configuredSecrets.includes(descriptor.key) && !Object.hasOwn(config, descriptor.key)}
              onChange={(value, valid) => {
                setInvalidSecrets((previous) => {
                  const next = new Set(previous)
                  if (valid) next.delete(descriptor.key)
                  else next.add(descriptor.key)
                  return next
                })
                if (valid) onChange(applyValueChange(config, descriptor.key, value))
              }}
            />
          )}
        />
      )
    }

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

    if (descriptor.dataKind === 'list' && descriptor.options) {
      return (
        <ListOptionsField
          key={descriptor.key}
          descriptor={descriptor}
          label={`${displayLabel}${requiredSuffix}`}
          description={description}
          dialogDescription={descriptionText}
          channelName={module.channelName}
          platform={moduleDisplayName}
          value={Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string') : []}
          onChange={(values) => onChange(applyValueChange(config, descriptor.key, values))}
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

    return (
      <SettingsRow
        key={descriptor.key}
        orientation="block"
        label={`${displayLabel}${requiredSuffix}`}
        description={description}
        control={(
          <Input
            value={toText(value)}
            placeholder={placeholder}
            onChange={(event) => onChange(applyValueChange(config, descriptor.key, event.target.value))}
          />
        )}
      />
    )
  }

  return (
    <div className={`${styles.form} ${SETTINGS_SURFACE_CLASS}`}>
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
          {moduleStatus.failureCode === 'channelStartFailed' && (
            <span style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>
              {t('channels.modules.failure.channelStartFailed')}
            </span>
          )}
          <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end' }}>
            {onStart && (
              <Button
                variant="primary"
                onClick={onStart}
              >
                {t('channels.modules.restart')}
              </Button>
            )}
          </div>
        </div>
      )}

      {onLoadLogs && (
        <Button variant="secondary" onClick={onLoadLogs} loading={logsLoading}>
          {t('channels.modules.logs.view')}
        </Button>
      )}
      {moduleLogLines !== null && (
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
            {moduleLogLines.length ? moduleLogLines.join('\n') : t('channels.plugins.noLogs')}
          </pre>
        </FieldCard>
      )}

      <fieldset className={styles.configurationGroups} disabled={readOnly || saving}>
        {descriptorGroups.map(({ group, descriptors: groupDescriptors }) => (
          <SettingsGroup key={group.id} title={resolveGroupLabel(group)}>
            {groupDescriptors.map((descriptor) => renderDescriptorField(descriptor))}
          </SettingsGroup>
        ))}
      </fieldset>

      <div className={styles.actions}>
        {onCancel && (
          <Button variant="secondary" onClick={onCancel}>
            {t('common.cancel')}
          </Button>
        )}
        {!readOnly && (
          <Button variant="primary" onClick={onSave} loading={saving} disabled={invalidSecrets.size > 0}>
            {saving ? t('channels.saving') : t('channels.save')}
          </Button>
        )}
      </div>
    </div>
  )
}
