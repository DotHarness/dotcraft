import { useState, type Dispatch, type JSX, type SetStateAction } from 'react'
import { ListChecks, TestTube2 } from 'lucide-react'
import type { ProviderBackendInfo } from '@dotcraft/sdk/contracts'
import { useT } from '../../../contexts/LocaleContext'
import { slugProviderId, uniqueProviderId } from '../../../utils/providerId'
import { ActionTooltip } from '../../ui/ActionTooltip'
import { Button } from '../../ui/Button'
import { Input } from '../../ui/Input'
import { PillSwitch } from '../../ui/PillSwitch'
import { StatusIndicator, type StatusTone } from '../../ui/StatusIndicator'
import { SettingsGroup, SettingsRow } from '../SettingsGroup'
import { settingsHintStyle, settingsSectionLabelStyle as sectionLabelStyle } from '../settingsTypography'
import { Select, type SelectOption } from '../../ui/Select'
import type { ProviderInfoWire } from '../providerInfo'
import { ProviderKeyFields } from './ProviderKeyFields'
import { ProviderSignIn } from './ProviderSignIn'
import {
  applyProviderConnection,
  canConfigureImageGeneration,
  CHATGPT_METHOD,
  providerConnectionMode,
  providerConnectionOptions,
  providerConnectionValue,
  selectedProviderConnection,
  type ProviderDraft
} from './providerDraft'
import styles from './ProviderEditor.module.css'

export interface ProviderTestResultWire {
  success: boolean
  providerId?: string | null
  protocol?: string
  models?: Array<{ id?: string; Id?: string }>
  errorCode?: string
  errorMessage?: string
}

interface ProviderEditorProps {
  draft: ProviderDraft
  setDraft: Dispatch<SetStateAction<ProviderDraft>>
  isNew: boolean
  savedProvider: ProviderInfoWire | null
  providers: readonly ProviderInfoWire[]
  backends: readonly ProviderBackendInfo[] | null
  managementEnabled: boolean
  testing: boolean
  testResult: ProviderTestResultWire | null
  saving: boolean
  deleting: boolean
  onTest: () => void
  onSave: () => void
  onDelete: (provider: ProviderInfoWire) => void
  prepareSignIn: () => Promise<string | null>
  onSignedIn: (providerId: string) => void | Promise<void>
  onSignedOut: (providerId: string) => void | Promise<void>
}

export function ProviderEditor({
  draft,
  setDraft,
  isNew,
  savedProvider,
  providers,
  backends,
  managementEnabled,
  testing,
  testResult,
  saving,
  deleting,
  onTest,
  onSave,
  onDelete,
  prepareSignIn,
  onSignedIn,
  onSignedOut
}: ProviderEditorProps): JSX.Element {
  const t = useT()
  const [signingIn, setSigningIn] = useState(false)
  const knownBackends = backends ?? []
  const connectionOptions = providerConnectionOptions(knownBackends, t)
  const connection = selectedProviderConnection(draft, connectionOptions)
  const connectionMode = providerConnectionMode(draft, knownBackends)
  const backendUnavailable = draft.backendId !== null && backends !== null && connection === null
  const selectOptions: SelectOption[] = connectionOptions.map(({ value, label, description }) => ({ value, label, description }))
  if (!connection && draft.backendId) {
    selectOptions.push({
      value: providerConnectionValue(draft),
      label: draft.backendId,
      description: t('settings.llm.providerBackendUnavailable'),
      disabled: true
    })
  }

  function selectConnection(value: string): void {
    const option = connectionOptions.find((candidate) => candidate.value === value)
    if (!option) return
    setDraft((current) => {
      const updated = applyProviderConnection(current, option)
      if (option.authMethod !== CHATGPT_METHOD || current.id.trim() || current.displayName.trim()) return updated
      return { ...updated, displayName: 'ChatGPT', id: uniqueProviderId('chatgpt', providers) }
    })
  }

  return (
    <div className={styles.editor}>
      <SettingsGroup flush>
        <div className={styles.grid}>
          <div>
            <label htmlFor="settings-provider-id" style={sectionLabelStyle()}>
              {t('settings.llm.field.id')}
            </label>
            <Input
              id="settings-provider-id"
              className={isNew ? undefined : styles.readOnly}
              value={draft.id}
              onChange={(e) => setDraft((current) => ({ ...current, id: slugProviderId(e.target.value) }))}
              readOnly={!isNew}
              mono
              placeholder="anthropic"
            />
            <div className={styles.hint} style={settingsHintStyle(false)}>
              {isNew ? t('settings.llm.field.idNewHint') : t('settings.llm.field.idEditHint')}
            </div>
          </div>
          <div>
            <label htmlFor="settings-provider-display-name" style={sectionLabelStyle()}>
              {t('settings.llm.field.displayName')}
            </label>
            <Input
              id="settings-provider-display-name"
              value={draft.displayName}
              onChange={(e) => {
                const displayName = e.target.value
                setDraft((current) => ({
                  ...current,
                  displayName,
                  id: isNew ? (current.id || slugProviderId(displayName)) : current.id
                }))
              }}
              placeholder="Anthropic"
            />
          </div>
        </div>
      </SettingsGroup>

      <SettingsGroup>
        <SettingsRow
          label={t('settings.llm.connectionType')}
          description={t('settings.llm.connectionTypeHint')}
          control={
            <Select
              value={providerConnectionValue(draft)}
              disabled={!isNew}
              ariaLabel={t('settings.llm.connectionType')}
              options={selectOptions}
              onValueChange={selectConnection}
            />
          }
        />
      </SettingsGroup>

      {connectionMode === 'signIn' && (
        <SettingsGroup title={t('settings.llm.connectionTitle')}>
          <ProviderSignIn
            providerId={savedProvider?.id ?? null}
            method={draft.authMethod}
            title={connection?.label ?? draft.backendId ?? ''}
            hint={t(draft.backendId ? 'settings.llm.signIn.hintBackend' : 'settings.llm.signIn.hintChatGpt')}
            unavailable={backendUnavailable}
            disabled={!managementEnabled || saving}
            prepare={prepareSignIn}
            onSignedIn={onSignedIn}
            onSignedOut={onSignedOut}
            onPendingChange={setSigningIn}
          />
        </SettingsGroup>
      )}
      {connectionMode === 'apiKey' && (
        <ProviderKeyFields draft={draft} setDraft={setDraft} savedProvider={savedProvider} />
      )}

      <SettingsGroup title={t('settings.group.advanced')}>
        <SettingsRow
          label={t('settings.llm.field.timeout')}
          description={t('settings.llm.field.timeoutHint')}
          htmlFor="settings-provider-timeout"
          orientation="block"
          control={
            <Input
              id="settings-provider-timeout"
              type="number"
              className="dc-plain-number"
              min={1}
              value={draft.networkTimeoutSeconds}
              onChange={(e) => setDraft((current) => ({ ...current, networkTimeoutSeconds: e.target.value }))}
              mono
              placeholder={t('settings.llm.field.timeoutPlaceholder')}
            />
          }
        />
        {canConfigureImageGeneration(draft) && (
          <SettingsRow
            label={t('settings.llm.field.imageGeneration')}
            description={t('settings.llm.field.imageGenerationHint')}
            controlMinWidth={48}
            control={
              <PillSwitch
                checked={draft.supportsImageGeneration}
                aria-label={t('settings.llm.field.imageGeneration')}
                onChange={(supportsImageGeneration) => {
                  setDraft((current) => ({
                    ...current,
                    supportsImageGeneration,
                    supportsImageGenerationTouched: true
                  }))
                }}
              />
            }
          />
        )}
      </SettingsGroup>

      <div className={styles.footer}>
        <div className={styles.footerStart}>
          {savedProvider && (
            <Button
              variant="danger"
              onClick={() => onDelete(savedProvider)}
              disabled={deleting || saving}
            >
              {deleting ? t('settings.llm.deletingProvider') : t('settings.llm.deleteProvider')}
            </Button>
          )}
          <ProviderTestStatus testing={testing} result={testResult} />
        </div>
        <div className={styles.footerActions}>
          <Button
            onClick={onTest}
            disabled={testing || !managementEnabled}
            iconLeft={<TestTube2 size={14} aria-hidden="true" />}
          >
            {testing ? t('settings.llm.testingProvider') : t('settings.llm.testProvider')}
          </Button>
          <Button
            variant="primary"
            onClick={onSave}
            disabled={saving || signingIn || !managementEnabled}
            iconLeft={<ListChecks size={14} aria-hidden="true" />}
          >
            {saving
              ? t('settings.llm.savingProvider')
              : isNew
                ? t('settings.llm.createProvider')
                : t('settings.llm.updateProvider')}
          </Button>
        </div>
      </div>
    </div>
  )
}

function ProviderTestStatus({ testing, result }: { testing: boolean; result: ProviderTestResultWire | null }): JSX.Element | null {
  const t = useT()
  if (testing) {
    return (
      <div role="status" aria-live="polite" className={styles.inlineStatus}>
        <StatusIndicator tone="pending" />
        <span className={styles.inlineStatusText}>{t('settings.llm.testRunningInline')}</span>
      </div>
    )
  }
  if (!result) return null
  const unsupported = result.errorCode === 'EndpointNotSupported'
  const tone: StatusTone = result.success ? 'success' : unsupported ? 'warning' : 'error'
  const message = result.success
    ? t('settings.llm.testSuccessInline', { count: result.models?.length ?? 0 })
    : unsupported
      ? t('settings.llm.testUnsupportedInline')
      : t('settings.llm.testFailedInline', {
          error: result.errorMessage ?? result.errorCode ?? t('settings.llm.unknownError')
        })
  return (
    <ActionTooltip label={message} wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flexShrink: 1 }}>
      <div role="status" aria-live="polite" className={styles.inlineStatus}>
        <StatusIndicator tone={tone} />
        <span className={styles.inlineStatusText}>{message}</span>
      </div>
    </ActionTooltip>
  )
}
