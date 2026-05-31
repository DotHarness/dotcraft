import { useMemo, useState } from 'react'
import type { DiscoveredModule, ModuleStatusEntry } from '../../../preload/api.d'
import type { AppLocale } from '../../../shared/locales'
import { useLocale, useT } from '../../contexts/LocaleContext'
import type { ChannelConnectionState } from './ChannelCard'
import { FieldCard, FormActions, SecretInput, StatusPill, formStyles } from './FormShared'
import { ToggleSwitch } from './ToggleSwitch'
import { FolderIcon } from '../ui/AppIcons'
import { IconButton } from '../ui/IconButton'

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
  wsAvailable: boolean
  onStart: () => void
  onStop: () => void
  starting: boolean
  qrDataUrl: string | null
  qrPhase: 'idle' | 'waitingForQr' | 'qrAvailable' | 'loginSuccess' | 'error'
  moduleLogLines: string[]
  logsLoading: boolean
  onLoadLogs: () => void
  hideHeader?: boolean
}

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
  wsAvailable,
  onStart,
  onStop,
  starting,
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
  const [showAdvanced, setShowAdvanced] = useState(false)
  const descriptors = useMemo(
    () =>
      module.configDescriptors.filter(
        (descriptor) =>
          descriptor.interactiveSetupOnly !== true && !descriptor.key.startsWith('dotcraft.')
      ),
    [module.configDescriptors]
  )
  const basicDescriptors = useMemo(
    () => descriptors.filter((descriptor) => descriptor.advanced !== true),
    [descriptors]
  )
  const advancedDescriptors = useMemo(
    () => descriptors.filter((descriptor) => descriptor.advanced === true),
    [descriptors]
  )
  const pill = resolveModulePill(moduleStatus, persistedEnabled, t)
  const enableChecked =
    persistedEnabled ||
    moduleStatus?.connected === true ||
    moduleStatus?.processState === 'starting' ||
    moduleStatus?.processState === 'running'
  const enableDisabled = starting || moduleStatus?.processState === 'stopping'
  const showQrPanel = module.requiresInteractiveSetup && qrPhase !== 'idle'
  const hasVariants = variantModules.length > 1
  const moduleDisplayName = resolveModuleDisplayName(module, locale)

  const resolveDescriptorLabel = (
    descriptor: DiscoveredModule['configDescriptors'][number]
  ): string => descriptor.localizedDisplayLabel?.[locale] ?? descriptor.displayLabel

  const resolveDescriptorDescription = (
    descriptor: DiscoveredModule['configDescriptors'][number]
  ): string => descriptor.localizedDescription?.[locale] ?? descriptor.description

  const renderDescriptorField = (descriptor: DiscoveredModule['configDescriptors'][number]): JSX.Element => {
    const value = getNestedValue(config, descriptor.key)
    const displayLabel = resolveDescriptorLabel(descriptor)
    const description = resolveDescriptorDescription(descriptor)
    const requiredSuffix = descriptor.required ? ` (${t('channels.modules.required')})` : ''
    const placeholder = descriptor.defaultValue === undefined ? undefined : String(descriptor.defaultValue)

    if (descriptor.dataKind === 'boolean') {
      return (
        <div key={descriptor.key} style={formStyles.fieldGroup}>
          <ToggleSwitch
            checked={value === true || (value === undefined && descriptor.defaultValue === true)}
            onChange={(checked) => {
              onChange(applyValueChange(config, descriptor.key, checked))
            }}
            label={`${displayLabel}${requiredSuffix}`}
            description={description}
          />
        </div>
      )
    }

    if (descriptor.dataKind === 'enum') {
      const enumValues = descriptor.enumValues ?? []
      const placeholderLabel =
        placeholder !== undefined && !enumValues.includes(placeholder) ? placeholder : ''
      return (
        <div key={descriptor.key} style={formStyles.fieldGroup}>
          <label style={formStyles.label}>{`${displayLabel}${requiredSuffix}`}</label>
          <select
            value={typeof value === 'string' ? value : ''}
            onChange={(event) => {
              onChange(applyValueChange(config, descriptor.key, event.target.value))
            }}
            onFocus={formStyles.inputFocus}
            onBlur={formStyles.inputBlur}
            style={formStyles.input}
          >
            <option value="" disabled={descriptor.required}>
              {placeholderLabel}
            </option>
            {enumValues.map((item) => (
              <option key={item} value={item}>
                {item}
              </option>
            ))}
          </select>
          {!!description && (
            <div style={{ marginTop: '6px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
              {description}
            </div>
          )}
        </div>
      )
    }

    if (descriptor.dataKind === 'list') {
      const textValue =
        listTextByKey[descriptor.key] ??
        (Array.isArray(value) ? value.filter((item): item is string => typeof item === 'string').join('\n') : '')
      return (
        <div key={descriptor.key} style={formStyles.fieldGroup}>
          <label style={formStyles.label}>{`${displayLabel}${requiredSuffix}`}</label>
          <textarea
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
            onFocus={formStyles.inputFocus}
            onBlur={formStyles.inputBlur}
            style={{ ...formStyles.input, minHeight: '90px', height: 'auto', padding: '8px 10px' }}
          />
          {!!description && (
            <div style={{ marginTop: '6px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
              {description}
            </div>
          )}
        </div>
      )
    }

    if (descriptor.dataKind === 'object') {
      const textValue = objectTextByKey[descriptor.key] ?? (value == null ? '' : JSON.stringify(value, null, 2))
      return (
        <div key={descriptor.key} style={formStyles.fieldGroup}>
          <label style={formStyles.label}>{`${displayLabel}${requiredSuffix}`}</label>
          <textarea
            value={textValue}
            placeholder={placeholder}
            onChange={(event) => {
              setObjectTextByKey((prev) => ({ ...prev, [descriptor.key]: event.target.value }))
            }}
            onBlur={(event) => {
              formStyles.inputBlur(event)
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
            onFocus={formStyles.inputFocus}
            style={{ ...formStyles.input, minHeight: '120px', height: 'auto', padding: '8px 10px' }}
          />
          {!!description && (
            <div style={{ marginTop: '6px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
              {description}
            </div>
          )}
        </div>
      )
    }

    if (descriptor.dataKind === 'number') {
      return (
        <div key={descriptor.key} style={formStyles.fieldGroup}>
          <label style={formStyles.label}>{`${displayLabel}${requiredSuffix}`}</label>
          <input
            type="number"
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
            onFocus={formStyles.inputFocus}
            onBlur={formStyles.inputBlur}
            style={formStyles.input}
          />
          {!!description && (
            <div style={{ marginTop: '6px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
              {description}
            </div>
          )}
        </div>
      )
    }

    if (descriptor.dataKind === 'path') {
      return (
        <div key={descriptor.key} style={formStyles.fieldGroup}>
          <label style={formStyles.label}>{`${displayLabel}${requiredSuffix}`}</label>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <input
              type="text"
              value={toText(value)}
              placeholder={placeholder}
              onChange={(event) => {
                onChange(applyValueChange(config, descriptor.key, event.target.value))
              }}
              onFocus={formStyles.inputFocus}
              onBlur={formStyles.inputBlur}
              style={{ ...formStyles.input, flex: 1 }}
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
          {!!description && (
            <div style={{ marginTop: '6px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
              {description}
            </div>
          )}
        </div>
      )
    }

    const isSecret = descriptor.dataKind === 'secret' || descriptor.masked
    return (
      <div key={descriptor.key} style={formStyles.fieldGroup}>
        <label style={formStyles.label}>{`${displayLabel}${requiredSuffix}`}</label>
        {isSecret ? (
          <SecretInput
            value={toText(value)}
            placeholder={placeholder}
            onChange={(nextValue) => {
              onChange(applyValueChange(config, descriptor.key, nextValue))
            }}
            onFocus={formStyles.inputFocus}
            onBlur={formStyles.inputBlur}
          />
        ) : (
          <input
            type="text"
            value={toText(value)}
            placeholder={placeholder}
            onChange={(event) => {
              onChange(applyValueChange(config, descriptor.key, event.target.value))
            }}
            onFocus={formStyles.inputFocus}
            onBlur={formStyles.inputBlur}
            style={formStyles.input}
          />
        )}
        {!!description && (
          <div style={{ marginTop: '6px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
            {description}
          </div>
        )}
      </div>
    )
  }

  return (
    <div style={{ maxWidth: '720px' }}>
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
            <select
              value={module.moduleId}
              disabled={variantSwitching}
              onChange={(event) => {
                onVariantChange?.(event.target.value)
              }}
              onFocus={formStyles.inputFocus}
              onBlur={formStyles.inputBlur}
              style={formStyles.input}
            >
              {variantModules.map((variant) => (
                <option key={variant.moduleId} value={variant.moduleId}>
                  {t('channels.modules.variant.option', {
                    name: resolveModuleDisplayName(variant, locale),
                    variant: variant.variant
                  })}
                </option>
              ))}
            </select>
          </div>
        </FieldCard>
      )}

      <FieldCard>
        <ToggleSwitch
          checked={enableChecked}
          disabled={enableDisabled}
          onChange={(checked) => {
            if (checked) {
              onStart()
            } else {
              onStop()
            }
          }}
          label={t('channels.modules.enable')}
        />
        {!wsAvailable && (
          <div style={{ marginTop: '8px', fontSize: '12px', color: 'var(--warning, #ff9f0a)' }}>
            {t('channels.modules.wsRequired')}
          </div>
        )}
      </FieldCard>

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
            {t('channels.modules.crashBanner', { code: String(moduleStatus.lastExitCode ?? 'unknown') })}
          </span>
          {moduleStatus.crashHint && (
            <span style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>{moduleStatus.crashHint}</span>
          )}
          {moduleStatus.lastStderrExcerpt && moduleStatus.lastStderrExcerpt.length > 0 && (
            <pre
              style={{
                margin: 0,
                padding: '8px',
                borderRadius: '6px',
                background: 'rgba(0, 0, 0, 0.2)',
                color: 'var(--text-secondary)',
                fontSize: '11px',
                lineHeight: 1.4,
                whiteSpace: 'pre-wrap',
                maxHeight: '180px',
                overflow: 'auto'
              }}
            >
              {moduleStatus.lastStderrExcerpt.join('\n')}
            </pre>
          )}
          <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end' }}>
            <button
              type="button"
              onClick={onLoadLogs}
              style={{
                border: '1px solid var(--border-default)',
                background: 'transparent',
                color: 'var(--text-primary)',
                borderRadius: '6px',
                padding: '4px 10px',
                fontSize: '12px',
                fontWeight: 600,
                cursor: 'pointer'
              }}
            >
              {logsLoading ? t('channels.modules.logs.loading') : t('channels.modules.logs.view')}
            </button>
            <button
              type="button"
              onClick={onStart}
              style={{
                border: '1px solid var(--error, #ff453a)',
                background: 'transparent',
                color: 'var(--error, #ff453a)',
                borderRadius: '6px',
                padding: '4px 10px',
                fontSize: '12px',
                fontWeight: 600,
                cursor: 'pointer'
              }}
            >
              {t('channels.modules.restart')}
            </button>
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
                <button
                  type="button"
                  onClick={onStart}
                  style={{
                    border: '1px solid var(--border-default)',
                    borderRadius: '6px',
                    background: 'transparent',
                    color: 'var(--text-primary)',
                    padding: '6px 12px',
                    cursor: 'pointer',
                    fontSize: '12px',
                    fontWeight: 600
                  }}
                >
                  {t('channels.modules.qr.retry')}
                </button>
              </>
            )}
          </div>
        </FieldCard>
      )}

      <FieldCard>
        {basicDescriptors.map((descriptor) => renderDescriptorField(descriptor))}
        {advancedDescriptors.length > 0 && (
          <div style={{ marginTop: basicDescriptors.length > 0 ? '2px' : 0 }}>
            <button
              type="button"
              onClick={() => setShowAdvanced((prev) => !prev)}
              style={{
                border: 'none',
                background: 'transparent',
                padding: 0,
                color: 'var(--text-secondary)',
                cursor: 'pointer',
                fontSize: '12px',
                fontWeight: 600
              }}
            >
              {showAdvanced
                ? t('channels.modules.hideAdvanced')
                : t('channels.modules.showAdvanced', { count: advancedDescriptors.length })}
            </button>
          </div>
        )}
        {showAdvanced && advancedDescriptors.map((descriptor) => renderDescriptorField(descriptor))}
      </FieldCard>

      <FormActions saving={saving} onSave={onSave} />
    </div>
  )
}
