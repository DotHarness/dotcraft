import { useEffect, useMemo, useState, type CSSProperties, type JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import type { MessageKey } from '../../../../../shared/locales'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { PillSwitch } from '../../../ui/PillSwitch'
import {
  EditableValueList,
  normalizeValueRows,
  rowsToValues,
  type ValueRow
} from '../../ui/EditableList'
import { AgentIcon } from './AgentIcon'
import {
  actionBarStyle,
  inputStyle,
  noticeStyle,
  pageDescriptionStyle,
  pageHeadingStyle,
  pageStyle,
  pillBadgeStyle,
  primaryButtonStyle,
  secondaryButtonStyle,
  dangerButtonStyle
} from './styles'
import {
  buildPresetOverrideWire,
  createPresetOverrideState,
  extractPresetExtraArgs,
  isPresetProfileName,
  type SubAgentProfileEntryWire
} from './wire'

interface PresetProfileDetailProps {
  profile: SubAgentProfileEntryWire
  externalCliSessionResumeEnabled: boolean
  toggling: boolean
  saving: boolean
  restoring: boolean
  onBack: () => void
  onToggleEnabled: (profile: SubAgentProfileEntryWire, nextEnabled: boolean) => void
  onSaveOverride: (profile: SubAgentProfileEntryWire, definition: SubAgentProfileEntryWire['definition']) => Promise<void> | void
  onRestoreDefaults: (profile: SubAgentProfileEntryWire) => Promise<void> | void
}

export function PresetProfileDetail({
  profile,
  externalCliSessionResumeEnabled,
  toggling,
  saving,
  restoring,
  onBack,
  onToggleEnabled,
  onSaveOverride,
  onRestoreDefaults
}: PresetProfileDetailProps): JSX.Element {
  const t = useT()
  const [showCustomize, setShowCustomize] = useState(profile.hasWorkspaceOverride)
  const [bin, setBin] = useState(profile.definition.bin ?? '')
  const [timeoutValue, setTimeoutValue] = useState<string>(
    profile.definition.timeout != null ? String(profile.definition.timeout) : ''
  )
  const [extraArgRows, setExtraArgRows] = useState<ValueRow[]>(() =>
    normalizeValueRows(extractPresetExtraArgs(profile))
  )
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  useEffect(() => {
    const initialState = createPresetOverrideState(profile)
    setBin(initialState.bin)
    setTimeoutValue(initialState.timeout)
    setExtraArgRows(normalizeValueRows(initialState.extraArgs))
    setErrorMessage(null)
    setShowCustomize(profile.hasWorkspaceOverride)
  }, [profile])

  const builtIn = profile.builtInDefaults ?? profile.definition
  const presetKey = isPresetProfileName(profile.name) ? profile.name : null
  const title = presetKey ? presetTitleFor(presetKey, t) : profile.name
  const description = presetKey ? presetDescriptionFor(presetKey, t) : ''
  const binaryLooksMissing =
    !profile.diagnostic.binaryResolved && profile.definition.runtime !== 'native'

  const canSubmit = useMemo(() => !saving && !restoring, [saving, restoring])

  async function handleSave(): Promise<void> {
    const extraArgs = rowsToValues(extraArgRows)
    const overrideState = { bin, extraArgs, timeout: timeoutValue }
    const built = buildPresetOverrideWire(profile, overrideState, t)
    if (!built.ok) {
      setErrorMessage(built.error)
      return
    }
    setErrorMessage(null)
    await onSaveOverride(profile, built.definition)
  }

  async function handleRestore(): Promise<void> {
    setErrorMessage(null)
    await onRestoreDefaults(profile)
  }

  return (
    <div style={pageStyle()}>
      <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
        <button type="button" onClick={onBack} style={secondaryButtonStyle()}>
          {t('settings.subAgents.back')}
        </button>
      </div>

      <div style={{ display: 'flex', alignItems: 'center', gap: '14px' }}>
        <AgentIcon name={profile.name} isBuiltIn={profile.isBuiltIn} size={44} />
        <div style={{ minWidth: 0, flex: 1 }}>
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
            <span style={pageHeadingStyle()}>{title}</span>
            {profile.hasWorkspaceOverride && (
              <span style={pillBadgeStyle('accent')}>
                {t('settings.subAgents.card.customizedBadge')}
              </span>
            )}
            {profile.isDefault && (
              <span style={pillBadgeStyle('neutral')}>{t('settings.subAgents.card.defaultBadge')}</span>
            )}
          </div>
          {description && <div style={pageDescriptionStyle()}>{description}</div>}
        </div>
      </div>

      <SettingsGroup>
        <SettingsRow
          label={t('settings.subAgents.preset.enableTitle')}
          description={t('settings.subAgents.preset.enableDescription')}
          control={
            <PillSwitch
              aria-label={t('settings.subAgents.toggleAria', { name: profile.name })}
              checked={profile.enabled}
              onChange={(next) => onToggleEnabled(profile, next)}
              disabled={toggling || profile.isDefault}
            />
          }
        />
      </SettingsGroup>

      <SettingsGroup title={t('settings.subAgents.preset.statusTitle')} flush>
        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
          <div>
            <span
              style={pillBadgeStyle(
                binaryLooksMissing ? 'warning' : 'success'
              )}
            >
              {binaryLooksMissing
                ? t('settings.subAgents.preset.binaryNotResolved')
                : t('settings.subAgents.preset.binaryResolved')}
            </span>
          </div>
          {profile.diagnostic.hiddenFromPrompt && profile.diagnostic.hiddenReason && (
            <div style={noticeStyle('warning')}>
              {t('settings.subAgents.preset.hiddenNotice', {
                reason: profile.diagnostic.hiddenReason
              })}
            </div>
          )}
          {profile.diagnostic.warnings.length > 0 && (
            <div style={noticeStyle('warning')}>
              <div style={{ fontWeight: 600, marginBottom: '4px' }}>
                {t('settings.subAgents.preset.warnings')}
              </div>
              <ul style={{ margin: 0, paddingInlineStart: '20px' }}>
                {profile.diagnostic.warnings.map((warning, index) => (
                  <li key={index}>{warning}</li>
                ))}
              </ul>
            </div>
          )}
        </div>
      </SettingsGroup>

      <SettingsGroup title={t('settings.subAgents.preset.runtimeInfoTitle')}>
        <SettingsRow
          label={t('settings.subAgents.preset.binaryLabel')}
          control={<code style={inlineCodeStyle()}>{builtIn.bin ?? '—'}</code>}
        />
        <SettingsRow
          label={t('settings.subAgents.preset.argsLabel')}
          orientation="block"
        >
          {builtIn.args && builtIn.args.length > 0 ? (
            <code style={blockCodeStyle()}>{builtIn.args.join(' ')}</code>
          ) : (
            <span style={{ color: 'var(--text-dimmed)', fontSize: '12px' }}>
              {t('settings.subAgents.preset.argsEmpty')}
            </span>
          )}
        </SettingsRow>
        {builtIn.permissionModeMapping && Object.keys(builtIn.permissionModeMapping).length > 0 && (
          <SettingsRow
            label={t('settings.subAgents.preset.permissionMappingTitle')}
            description={t('settings.subAgents.preset.permissionMappingHint')}
            orientation="block"
          >
            <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
              {Object.entries(builtIn.permissionModeMapping).map(([key, value]) => (
                <div
                  key={key}
                  style={{
                    display: 'grid',
                    gridTemplateColumns: '160px 1fr',
                    gap: '10px',
                    alignItems: 'center'
                  }}
                >
                  <span style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>{key}</span>
                  <code style={inlineCodeStyle()}>{value}</code>
                </div>
              ))}
            </div>
          </SettingsRow>
        )}
        {profile.definition.runtime === 'cli-oneshot' && (
          <SettingsRow
            label={t('settings.subAgents.preset.resumeTitle')}
            description={t('settings.subAgents.preset.resumeDescription')}
            control={
              <span
                style={pillBadgeStyle(
                  profile.definition.supportsResume && externalCliSessionResumeEnabled
                    ? 'success'
                    : profile.definition.supportsResume
                      ? 'warning'
                      : 'neutral'
                )}
              >
                {profile.definition.supportsResume
                  ? externalCliSessionResumeEnabled
                    ? t('settings.subAgents.preset.resumeEnabled')
                    : t('settings.subAgents.preset.resumeSupportedButOff')
                  : t('settings.subAgents.preset.resumeUnsupported')}
              </span>
            }
          />
        )}
      </SettingsGroup>

      {!showCustomize ? (
        <div style={actionBarStyle()}>
          <button
            type="button"
            onClick={() => setShowCustomize(true)}
            style={secondaryButtonStyle()}
          >
            {t('settings.subAgents.preset.customize')}
          </button>
          {profile.hasWorkspaceOverride && (
            <button
              type="button"
              onClick={handleRestore}
              style={dangerButtonStyle(restoring)}
              disabled={restoring}
            >
              {restoring ? t('settings.subAgents.deleting') : t('settings.subAgents.preset.restoreDefaults')}
            </button>
          )}
        </div>
      ) : (
        <SettingsGroup
          title={t('settings.subAgents.preset.customizeTitle')}
          description={t('settings.subAgents.preset.customizeHint')}
        >
          <SettingsRow
            label={t('settings.subAgents.preset.overrideBin')}
            description={t('settings.subAgents.preset.overrideBinHint', {
              default: builtIn.bin ?? '—'
            })}
            orientation="block"
          >
            <input
              type="text"
              value={bin}
              onChange={(event) => setBin(event.target.value)}
              placeholder={builtIn.bin ?? ''}
              style={inputStyle(true)}
              data-testid="subagent-preset-bin-input"
            />
          </SettingsRow>
          <SettingsRow
            label={t('settings.subAgents.preset.overrideExtraArgs')}
            description={t('settings.subAgents.preset.overrideExtraArgsHint')}
            orientation="block"
          >
            <EditableValueList
              rows={extraArgRows}
              setRows={setExtraArgRows}
              placeholder={t('settings.subAgents.preset.overrideExtraArgsPlaceholder')}
            />
          </SettingsRow>
          <SettingsRow
            label={t('settings.subAgents.preset.overrideTimeout')}
            description={t('settings.subAgents.preset.overrideTimeoutHint')}
            orientation="block"
          >
            <input
              type="number"
              min={1}
              value={timeoutValue}
              onChange={(event) => setTimeoutValue(event.target.value)}
              style={{ ...inputStyle(), width: '160px' }}
              data-testid="subagent-preset-timeout-input"
            />
          </SettingsRow>

          {errorMessage && <div style={noticeStyle('error')}>{errorMessage}</div>}

          <div style={{ ...actionBarStyle(), padding: '12px 16px' }}>
            <button
              type="button"
              onClick={() => {
                setShowCustomize(false)
                setErrorMessage(null)
                const initial = createPresetOverrideState(profile)
                setBin(initial.bin)
                setTimeoutValue(initial.timeout)
                setExtraArgRows(normalizeValueRows(initial.extraArgs))
              }}
              style={secondaryButtonStyle(saving)}
              disabled={saving}
            >
              {t('settings.subAgents.cancel')}
            </button>
            {profile.hasWorkspaceOverride && (
              <button
                type="button"
                onClick={handleRestore}
                style={dangerButtonStyle(restoring)}
                disabled={restoring}
              >
                {restoring
                  ? t('settings.subAgents.deleting')
                  : t('settings.subAgents.preset.restoreDefaults')}
              </button>
            )}
            <button
              type="button"
              onClick={handleSave}
              style={primaryButtonStyle(!canSubmit)}
              disabled={!canSubmit}
            >
              {saving ? t('settings.subAgents.saving') : t('settings.subAgents.preset.saveOverride')}
            </button>
          </div>
        </SettingsGroup>
      )}
    </div>
  )
}

function presetTitleFor(
  name: 'native' | 'codex-cli' | 'cursor-cli',
  t: (key: MessageKey | string, vars?: Record<string, string | number>) => string
): string {
  if (name === 'codex-cli') return t('settings.subAgents.preset.codex.title')
  if (name === 'cursor-cli') return t('settings.subAgents.preset.cursor.title')
  return t('settings.subAgents.preset.native.title')
}

function presetDescriptionFor(
  name: 'native' | 'codex-cli' | 'cursor-cli',
  t: (key: MessageKey | string, vars?: Record<string, string | number>) => string
): string {
  if (name === 'codex-cli') return t('settings.subAgents.preset.codex.description')
  if (name === 'cursor-cli') return t('settings.subAgents.preset.cursor.description')
  return t('settings.subAgents.preset.native.description')
}

function inlineCodeStyle(): CSSProperties {
  return {
    padding: '2px 6px',
    borderRadius: '6px',
    background: 'var(--bg-tertiary)',
    fontFamily: 'var(--font-mono)',
    fontSize: '12px',
    color: 'var(--text-primary)'
  }
}

function blockCodeStyle(): CSSProperties {
  return {
    display: 'block',
    padding: '10px 12px',
    borderRadius: '8px',
    background: 'var(--bg-tertiary)',
    fontFamily: 'var(--font-mono)',
    fontSize: '12px',
    color: 'var(--text-primary)',
    overflowX: 'auto'
  }
}
