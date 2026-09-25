import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties, type MutableRefObject, type ReactNode, type Ref } from 'react'
import { ArrowLeft, ArrowRight, Check, Folder } from 'lucide-react'
import { normalizeLocale, SUPPORTED_LOCALES, type AppLocale } from '../../shared/locales'
import { useLocale, useSetUiLocale, useT } from '../contexts/LocaleContext'
import type {
  WorkspaceSetupBootstrapImportSource,
  WorkspaceSetupBootstrapImportSourceId,
  WorkspaceSetupProviderDraft,
  WorkspaceSetupProviderProtocol,
  WorkspaceSetupProviderSummary,
  WorkspaceSetupRequest,
  WorkspaceSetupRunResult,
  WorkspaceStatusPayload
} from '../../preload/api.d'
import { SecretInput } from './channels/FormShared'
import { ToggleSwitch } from './channels/ToggleSwitch'
import { SettingsSelect } from './settings/ui/SettingsSelect'
import { ProviderProtocolIcon } from './settings/panels/ProviderProtocolIcon'
import { ProviderModelSummary } from './settings/ProviderModelSummary'
import { ActionTooltip } from './ui/ActionTooltip'
import { Button } from './ui/Button'
import { Input } from './ui/Input'
import { BootstrapImportSourceIcon } from './setup/BootstrapImportSourceIcon'
import { centeredLaunchLogoRect, elementToLaunchLogoRect, type LaunchLogoRect } from './WorkspaceLaunchTransition'
import {
  ANTHROPIC_PROTOCOL,
  defaultProviderEndpoint,
  DESKTOP_PROVIDER_PROTOCOLS,
  OPENAI_RESPONSES_PROTOCOL,
  providerProtocolLabel
} from '../../shared/providerProtocols'
import { useSetupModelCatalog } from '../hooks/useSetupModelCatalog'
import { slugProviderId, uniqueProviderId } from '../utils/providerId'
import {
  cloneModelPreference,
  createManualModelPreference,
  type ModelPreference
} from '../../shared/modelPreference'
import {
  type ModelCatalogItem
} from '../stores/modelCatalogStore'
import {
  PreferenceModelPicker
} from './conversation/PreferenceModelPicker'

interface WorkspaceSetupWizardProps {
  workspacePath: string
  workspaceStatus: WorkspaceStatusPayload
  hideLogo?: boolean
  deferContent?: boolean
  logoAnchorRef?: Ref<HTMLDivElement>
  onRunSetup?: (request: WorkspaceSetupRequest, context: WorkspaceSetupSubmitContext) => Promise<void | WorkspaceSetupRunResult>
  onChooseDifferentWorkspace: () => void
  onCancel: () => void
}

export interface WorkspaceSetupSubmitContext {
  logoRect: LaunchLogoRect
  logoSrc: string
}

type WizardStep = number
type ProviderChoice = 'existing' | 'openai-template' | 'anthropic-template' | 'custom'

const providerCardChoices: ProviderChoice[] = [
  'existing',
  'openai-template',
  'anthropic-template',
  'custom'
]

const setupLogoUrl = new URL('../../../resources/dotcraft.svg', import.meta.url).toString()

function cardStyle(active: boolean): CSSProperties {
  return {
    border: active ? '1px solid var(--accent)' : '1px solid var(--border-default)',
    borderRadius: '8px',
    background: active ? 'var(--bg-tertiary)' : 'var(--bg-secondary)',
    padding: '14px',
    cursor: 'pointer'
  }
}

function isValidHttpUrl(value: string): boolean {
  try {
    const parsed = new URL(value.trim())
    return parsed.protocol === 'http:' || parsed.protocol === 'https:'
  } catch {
    return false
  }
}

function templateDraft(
  protocol: WorkspaceSetupProviderProtocol,
  providers: WorkspaceSetupProviderSummary[]
): WorkspaceSetupProviderDraft {
  const id = protocol === ANTHROPIC_PROTOCOL
    ? uniqueProviderId('anthropic', providers)
    : uniqueProviderId('openai', providers)
  return {
    id,
    displayName: providerProtocolLabel(protocol),
    protocol,
    apiKey: '',
    endPoint: defaultProviderEndpoint(protocol),
    networkTimeoutSeconds: null,
    authMethod: 'apiKey'
  }
}

function selectedExistingProvider(
  providers: WorkspaceSetupProviderSummary[],
  selectedProviderId: string
): WorkspaceSetupProviderSummary | null {
  return providers.find((provider) => provider.id === selectedProviderId) ?? providers[0] ?? null
}

export function WorkspaceSetupWizard({
  workspacePath,
  workspaceStatus,
  hideLogo = false,
  deferContent = false,
  logoAnchorRef,
  onRunSetup,
  onChooseDifferentWorkspace,
  onCancel
}: WorkspaceSetupWizardProps): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const setUiLocale = useSetUiLocale()
  const providers = workspaceStatus.providers ?? []
  const userConfigDefaults = workspaceStatus.userConfigDefaults
  const bootstrapImportSources = workspaceStatus.bootstrapImportSources ?? []
  const hasBootstrapImportSources = bootstrapImportSources.length > 0
  const [step, setStep] = useState<WizardStep>(0)
  const [selectedBootstrapImportSourceId, setSelectedBootstrapImportSourceId] =
    useState<WorkspaceSetupBootstrapImportSourceId | null>(() => bootstrapImportSources[0]?.id ?? null)
  const [providerChoice, setProviderChoice] = useState<ProviderChoice>(() =>
    providers.length > 0 ? 'existing' : 'openai-template'
  )
  const [selectedProviderId, setSelectedProviderId] = useState(
    userConfigDefaults?.providerId?.trim() || providers[0]?.id || ''
  )
  const [openAiDraft, setOpenAiDraft] = useState<WorkspaceSetupProviderDraft>(() =>
    templateDraft(OPENAI_RESPONSES_PROTOCOL, providers)
  )
  const [anthropicDraft, setAnthropicDraft] = useState<WorkspaceSetupProviderDraft>(() =>
    templateDraft(ANTHROPIC_PROTOCOL, providers)
  )
  const [customDraft, setCustomDraft] = useState<WorkspaceSetupProviderDraft>(() => ({
    id: uniqueProviderId('provider', providers),
    displayName: '',
    protocol: OPENAI_RESPONSES_PROTOCOL,
    apiKey: '',
    endPoint: defaultProviderEndpoint(OPENAI_RESPONSES_PROTOCOL),
    networkTimeoutSeconds: null
  }))
  const [customTimeoutDraft, setCustomTimeoutDraft] = useState('')
  const [preference, setPreference] = useState<ModelPreference>(() =>
    userConfigDefaults?.preference
      ? cloneModelPreference(userConfigDefaults.preference)
      : createManualModelPreference(userConfigDefaults?.model?.trim() || '')
  )
  const model = preference.model
  const [modelDirty, setModelDirty] = useState(false)
  const [defaultScopeDirty, setDefaultScopeDirty] = useState(false)
  const [setAsUserDefault, setSetAsUserDefault] = useState(providers.length === 0)
  const [submitting, setSubmitting] = useState(false)
  const [submitError, setSubmitError] = useState<string | null>(null)
  const [submitWarning, setSubmitWarning] = useState<string | null>(null)
  const [switchingDisplayLocale, setSwitchingDisplayLocale] = useState(false)
  const logoNodeRef = useRef<HTMLDivElement | null>(null)
  const localizedDisplayLanguage =
    SUPPORTED_LOCALES.find((item) => item.value === locale)?.nativeName ?? locale

  const bootstrapImportSourceIds = bootstrapImportSources.map((source) => source.id).join('|')

  useEffect(() => {
    setSelectedBootstrapImportSourceId((current) => {
      if (bootstrapImportSources.length === 0) return null
      if (current && bootstrapImportSources.some((source) => source.id === current)) return current
      return bootstrapImportSources[0]?.id ?? null
    })
  }, [bootstrapImportSourceIds])

  const steps = useMemo(
    () => [
      t('setupWizard.step.welcome'),
      ...(hasBootstrapImportSources ? [t('setupWizard.step.import')] : []),
      t('setupWizard.step.config'),
      t('setupWizard.step.confirm')
    ],
    [hasBootstrapImportSources, t]
  )
  const importStepIndex = hasBootstrapImportSources ? 1 : -1
  const configStepIndex = hasBootstrapImportSources ? 2 : 1
  const confirmStepIndex = hasBootstrapImportSources ? 3 : 2
  const lastStepIndex = steps.length - 1
  const selectedBootstrapImportSource =
    bootstrapImportSources.find((source) => source.id === selectedBootstrapImportSourceId) ?? null

  useEffect(() => {
    setStep((current) => Math.min(current, lastStepIndex))
  }, [lastStepIndex])

  const activeDraft = providerChoice === 'openai-template'
    ? openAiDraft
    : providerChoice === 'anthropic-template'
      ? anthropicDraft
      : providerChoice === 'custom'
        ? customDraft
        : null
  const activeExistingProvider = selectedExistingProvider(providers, selectedProviderId)
  const activeProtocol = activeDraft?.protocol ?? activeExistingProvider?.protocol ?? OPENAI_RESPONSES_PROTOCOL
  const endpointInvalid =
    activeDraft != null &&
    activeDraft.endPoint.trim().length > 0 &&
    !isValidHttpUrl(activeDraft.endPoint)
  const { modelLoadState, modelCatalog, chatGptLoginPending, loginError, loginChatGptForSetup, retry } = useSetupModelCatalog({
    step, configStepIndex, providerChoice, activeDraft, activeExistingProvider, modelDirty, userConfigDefaults, setPreference
  })
  const modelListLoading = modelLoadState === 'loading'
  const modelSelectAvailable =
    modelLoadState === 'ready' &&
    modelCatalog.length > 0
  // The catalog is fetched with the OAuth token, so a loaded list is the wizard's only signal that sign-in took.
  const chatGptSignInState: ChatGptSignInState =
    chatGptLoginPending ? 'pending' : modelLoadState === 'ready' ? 'signedIn' : 'required'
  const canAdvanceFromConfig =
    model.trim().length > 0 &&
    (providerChoice !== 'existing' || activeExistingProvider != null) &&
    (providerChoice === 'existing' || (
      activeDraft != null &&
      activeDraft.id.trim().length > 0 &&
      activeDraft.protocol.trim().length > 0 &&
      !endpointInvalid
    ))
  const nextDisabled = submitting || (step === configStepIndex && !canAdvanceFromConfig)
  const nextLabel = step < lastStepIndex
    ? t('setupWizard.button.next')
    : submitting
      ? t('setupWizard.button.creating')
      : t('setupWizard.button.create')

  const setLogoAnchor = useCallback((node: HTMLDivElement | null): void => {
    logoNodeRef.current = node
    if (typeof logoAnchorRef === 'function') {
      logoAnchorRef(node)
      return
    }
    if (logoAnchorRef) {
      const mutableLogoAnchorRef = logoAnchorRef as MutableRefObject<HTMLDivElement | null>
      mutableLogoAnchorRef.current = node
    }
  }, [logoAnchorRef])

  useEffect(() => {
    const nextProviderId = userConfigDefaults?.providerId?.trim() || providers[0]?.id || ''
    setSelectedProviderId((current) => current || nextProviderId)
  }, [providers, userConfigDefaults?.providerId])

  useEffect(() => {
    setOpenAiDraft((current) => current.id.trim() ? current : templateDraft(OPENAI_RESPONSES_PROTOCOL, providers))
    setAnthropicDraft((current) => current.id.trim() ? current : templateDraft(ANTHROPIC_PROTOCOL, providers))
  }, [providers])

  useEffect(() => {
    if (!modelDirty) {
      setPreference(userConfigDefaults?.preference
        ? cloneModelPreference(userConfigDefaults.preference)
        : createManualModelPreference(userConfigDefaults?.model?.trim() || ''))
    }
  }, [modelDirty, userConfigDefaults?.model, userConfigDefaults?.preference])

  useEffect(() => {
    if (defaultScopeDirty) return
    setSetAsUserDefault(providerChoice === 'openai-template' || providerChoice === 'anthropic-template' || providerChoice === 'custom')
  }, [defaultScopeDirty, providerChoice])


  async function handleSubmit(): Promise<void> {
    const request = buildSetupRequest()
    const runSetup = onRunSetup ?? ((setupRequest: WorkspaceSetupRequest) => window.api.workspace.runSetup(setupRequest))
    const logoRect = elementToLaunchLogoRect(logoNodeRef.current) ?? centeredLaunchLogoRect()
    setSubmitting(true)
    setSubmitError(null)
    setSubmitWarning(null)
    try {
      const setupResult = await runSetup(request, {
        logoRect,
        logoSrc: setupLogoUrl
      })
      if (setupResult && typeof setupResult === 'object' && setupResult.bootstrapImport?.warning) {
        setSubmitWarning(t('setupWizard.import.warningInline'))
      }
    } catch (err) {
      setSubmitError(err instanceof Error ? err.message : String(err))
    } finally {
      setSubmitting(false)
    }
  }

  function buildSetupRequest(): WorkspaceSetupRequest {
    if (providerChoice === 'existing') {
      return {
        model: model.trim(),
        preference: cloneModelPreference(preference),
        providerMode: 'existing',
        providerId: activeExistingProvider?.id ?? selectedProviderId.trim(),
        setAsUserDefault,
        ...(selectedBootstrapImportSourceId ? { bootstrapImportSourceId: selectedBootstrapImportSourceId } : {})
      }
    }

    const draft = activeDraft ?? templateDraft(activeProtocol, providers)
    return {
      model: model.trim(),
      preference: cloneModelPreference(preference),
      providerMode: 'create',
      provider: {
        ...draft,
        id: draft.id.trim(),
        displayName: draft.displayName.trim() || draft.id.trim(),
        apiKey: draft.apiKey.trim(),
        endPoint: draft.endPoint.trim(),
        networkTimeoutSeconds: draft.networkTimeoutSeconds ?? null
      },
      setAsUserDefault,
      ...(selectedBootstrapImportSourceId ? { bootstrapImportSourceId: selectedBootstrapImportSourceId } : {})
    }
  }

  async function handleDisplayLocaleChange(next: AppLocale): Promise<void> {
    const normalized = normalizeLocale(next)
    if (normalized === locale || switchingDisplayLocale) return
    setSwitchingDisplayLocale(true)
    setUiLocale(normalized)
    try {
      await window.api.settings.set({ locale: normalized })
    } catch {
      // Non-fatal: keep current in-memory UI locale for this onboarding flow.
    } finally {
      setSwitchingDisplayLocale(false)
    }
  }

  function goBack(): void {
    if (submitting) return
    if (step === 0) {
      onCancel()
      return
    }
    setStep((prev) => Math.max(0, prev - 1))
  }

  function goNext(): void {
    if (submitting) return
    if (step === configStepIndex && !canAdvanceFromConfig) {
      return
    }
    setStep((prev) => Math.min(lastStepIndex, prev + 1))
  }

  function updateActiveDraft(partial: Partial<WorkspaceSetupProviderDraft>): void {
    if (providerChoice === 'openai-template') {
      setOpenAiDraft((draft) => ({ ...draft, ...partial }))
      return
    }
    if (providerChoice === 'anthropic-template') {
      setAnthropicDraft((draft) => ({ ...draft, ...partial }))
      return
    }
    setCustomDraft((draft) => ({ ...draft, ...partial }))
  }

  return (
    <div
      className={deferContent ? 'setup-wizard-shell setup-wizard-shell--handoff' : 'setup-wizard-shell'}
      style={{
        display: 'grid',
        gridTemplateColumns: '44px minmax(0, 760px) 44px',
        justifyContent: 'center',
        alignItems: 'center',
        gap: '16px',
        height: '100%',
        overflowY: 'auto',
        background: 'var(--welcome-surface)',
        padding: '28px 20px 36px',
        boxSizing: 'border-box'
      }}
    >
      <WizardNavButton
        direction="back"
        label={step === 0 ? t('setupWizard.button.cancel') : t('setupWizard.button.back')}
        disabled={submitting}
        onClick={goBack}
      />
      <div style={{ width: '100%', maxWidth: '760px' }}>
        <div className="setup-wizard-title-block" style={{ marginBottom: '18px' }}>
          <div
            className={hideLogo ? 'setup-wizard-logo-anchor setup-wizard-logo-anchor--hidden' : 'setup-wizard-logo-anchor'}
            aria-hidden="true"
            ref={setLogoAnchor}
          >
            <img
              src={setupLogoUrl}
              alt=""
              width={96}
              height={96}
              draggable={false}
              className="setup-wizard-logo-image"
            />
          </div>
          <div
            className="tool-running-gradient-text setup-wizard-kicker"
            style={{
              fontSize: '12px',
              fontWeight: 600,
              textTransform: 'uppercase',
              letterSpacing: '0.05em',
              marginBottom: '8px',
              textAlign: 'center'
            }}
          >
            {t('setupWizard.title')}
          </div>
          <nav
            className="setup-stepper-row"
            aria-label={t('setupWizard.title')}
          >
            <div className="setup-stepper-compact-heading">
              <span>
                {t('setupWizard.stepCount', {
                  n: step + 1,
                  total: steps.length
                })}
              </span>
              <strong>{steps[step]}</strong>
            </div>
            <ol
              className="setup-stepper-list"
              style={{
                gridTemplateColumns: `repeat(${steps.length}, minmax(0, 1fr))`
              }}
            >
              {steps.map((label, idx) => {
                const active = idx === step
                const completed = idx < step
                const canReturn = completed && !submitting
                return (
                  <li
                    key={label}
                    className="setup-stepper-item"
                    data-state={completed ? 'complete' : active ? 'active' : 'future'}
                  >
                    <button
                      type="button"
                      className="setup-stepper-button"
                      disabled={!canReturn}
                      aria-label={label}
                      aria-current={active ? 'step' : undefined}
                      onClick={() => {
                        if (canReturn) setStep(idx)
                      }}
                    >
                      <span className="setup-stepper-marker" aria-hidden="true">
                        {completed ? <Check size={14} strokeWidth={2.4} /> : idx + 1}
                      </span>
                      <span className="setup-stepper-label">{label}</span>
                    </button>
                  </li>
                )
              })}
            </ol>
          </nav>
        </div>

        <div
          key={step}
          className="setup-wizard-step-panel"
          style={{
            border: '1px solid var(--border-default)',
            borderRadius: '12px',
            background: 'var(--bg-secondary)',
            padding: '22px'
          }}
        >
          {step === 0 && (
            <WelcomeStep
              workspacePath={workspacePath}
              locale={locale}
              switchingDisplayLocale={switchingDisplayLocale}
              onDisplayLocaleChange={handleDisplayLocaleChange}
              onChooseDifferentWorkspace={onChooseDifferentWorkspace}
            />
          )}

          {hasBootstrapImportSources && step === importStepIndex && (
            <BootstrapImportStep
              sources={bootstrapImportSources}
              selectedSourceId={selectedBootstrapImportSourceId}
              onChange={setSelectedBootstrapImportSourceId}
            />
          )}

          {step === configStepIndex && (
            <div>
              <h1 style={{ margin: '0 0 10px', fontSize: '24px', fontWeight: 700 }}>
                {t('setupWizard.config.title')}
              </h1>
              <p style={{ margin: '0 0 18px', color: 'var(--text-secondary)', lineHeight: 1.6 }}>
                {t('setupWizard.config.description')}
              </p>

              {workspaceStatus.hasUserConfig && providers.length > 0 && (
                <div
                  style={{
                    marginBottom: '18px',
                    padding: '12px 14px',
                    borderRadius: '8px',
                    border: '1px solid var(--border-default)',
                    background: 'var(--bg-primary)',
                    color: 'var(--text-secondary)',
                    fontSize: '13px',
                    lineHeight: 1.6
                  }}
                >
                  {t('setupWizard.config.userConfigDetected')}
                </div>
              )}

              <div style={{ display: 'grid', gap: '10px', marginBottom: '16px' }}>
                {providerCardChoices.map((choice) => {
                  const disabled = choice === 'existing' && providers.length === 0
                  const active = providerChoice === choice
                  const providerIcon = choice === 'openai-template'
                    ? <ProviderProtocolIcon protocol={OPENAI_RESPONSES_PROTOCOL} size={30} />
                    : choice === 'anthropic-template'
                      ? <ProviderProtocolIcon protocol={ANTHROPIC_PROTOCOL} size={30} />
                      : null
                  return (
                    <button
                      key={choice}
                      type="button"
                      disabled={disabled}
                      onClick={() => {
                        if (!disabled) setProviderChoice(choice)
                      }}
                      style={{
                        ...cardStyle(active),
                        display: 'grid',
                        gridTemplateColumns: providerIcon ? 'auto minmax(0, 1fr)' : 'minmax(0, 1fr)',
                        alignItems: 'center',
                        gap: '12px',
                        textAlign: 'left',
                        opacity: disabled ? 0.55 : 1,
                        cursor: disabled ? 'default' : 'pointer'
                      }}
                    >
                      {providerIcon}
                      <div style={{ minWidth: 0 }}>
                        <div style={{ fontSize: '14px', fontWeight: 600, color: 'var(--text-primary)' }}>
                          {t(`setupWizard.providerChoice.${choice}.title`)}
                        </div>
                        <div style={{ marginTop: '6px', fontSize: '13px', lineHeight: 1.55, color: 'var(--text-secondary)' }}>
                          {t(`setupWizard.providerChoice.${choice}.description`)}
                        </div>
                      </div>
                    </button>
                  )
                })}
              </div>

              {providerChoice === 'existing' && (
                <ExistingProviderForm
                  providers={providers}
                  selectedProviderId={selectedProviderId}
                  selectedProvider={activeExistingProvider}
                  signInState={chatGptSignInState}
                  loginError={loginError}
                  onLoginChatGpt={() => { void loginChatGptForSetup() }}
                  onChange={setSelectedProviderId}
                />
              )}

              {(providerChoice === 'openai-template' || providerChoice === 'anthropic-template') && activeDraft && (
                <TemplateProviderForm
                  draft={activeDraft}
                  signInState={chatGptSignInState}
                  loginError={loginError}
                  onLoginChatGpt={() => { void loginChatGptForSetup() }}
                  onChange={updateActiveDraft}
                />
              )}

              {providerChoice === 'custom' && (
                <CustomProviderForm
                  draft={customDraft}
                  timeoutDraft={customTimeoutDraft}
                  signInState={chatGptSignInState}
                  loginError={loginError}
                  onLoginChatGpt={() => { void loginChatGptForSetup() }}
                  onChange={(partial) => {
                    setCustomDraft((draft) => ({ ...draft, ...partial }))
                  }}
                  onTimeoutChange={(value) => {
                    setCustomTimeoutDraft(value)
                    setCustomDraft((draft) => ({
                      ...draft,
                      networkTimeoutSeconds: value.trim() ? Number(value.trim()) : null
                    }))
                  }}
                />
              )}

              {endpointInvalid && (
                <div style={{ marginTop: '8px', fontSize: '12px', color: 'var(--error)' }}>
                  {t('setupWizard.validation.endpoint')}
                </div>
              )}

              <ModelField
                preference={preference}
                models={modelCatalog}
                modelListLoading={modelListLoading}
                modelSelectAvailable={modelSelectAvailable}
                modelLoadState={modelLoadState}
                onRetry={retry}
                onChange={(nextPreference) => {
                  setModelDirty(true)
                  setPreference(nextPreference)
                }}
              />

              <div
                style={{
                  marginTop: '14px',
                  padding: '12px 14px',
                  borderRadius: '8px',
                  border: '1px solid var(--border-default)',
                  background: 'var(--bg-primary)'
                }}
              >
                <ToggleSwitch
                  checked={setAsUserDefault}
                  onChange={(checked) => {
                    setDefaultScopeDirty(true)
                    setSetAsUserDefault(checked)
                  }}
                  label={t('setupWizard.defaultScope.title')}
                  description={t('setupWizard.defaultScope.description')}
                />
              </div>
            </div>
          )}

          {step === confirmStepIndex && (
            <ConfirmStep
              displayLanguage={localizedDisplayLanguage}
              providerName={
                providerChoice === 'existing'
                  ? activeExistingProvider?.displayName ?? selectedProviderId
                  : activeDraft?.displayName || activeDraft?.id || ''
              }
              providerId={
                providerChoice === 'existing'
                  ? activeExistingProvider?.id ?? selectedProviderId
                  : activeDraft?.id ?? ''
              }
              protocol={activeProtocol}
              preference={preference}
              setAsUserDefault={setAsUserDefault}
              bootstrapImportSource={selectedBootstrapImportSource}
              submitError={submitError}
              submitWarning={submitWarning}
            />
          )}
        </div>
      </div>
      <WizardNavButton
        direction="next"
        label={nextLabel}
        disabled={nextDisabled}
        primary
        onClick={() => {
          if (step < lastStepIndex) {
            goNext()
            return
          }
          void handleSubmit()
        }}
      />
    </div>
  )
}

function WizardNavButton({
  direction,
  label,
  disabled,
  primary = false,
  onClick
}: {
  direction: 'back' | 'next'
  label: string
  disabled: boolean
  primary?: boolean
  onClick(): void
}): JSX.Element {
  const Icon = direction === 'back' ? ArrowLeft : ArrowRight
  return (
    <ActionTooltip
      label={label}
      placement={direction === 'back' ? 'right' : 'left'}
      wrapperStyle={{
        alignSelf: 'start',
        justifySelf: 'center',
        position: 'sticky',
        top: 'calc(50% - 22px)',
        zIndex: 1
      }}
    >
      <button
        type="button"
        className={`workspace-setup-nav-button workspace-setup-nav-button--${direction}${primary ? ' workspace-setup-nav-button--primary' : ''}`}
        aria-label={label}
        disabled={disabled}
        onClick={onClick}
        style={{
          '--setup-nav-final-opacity': disabled ? 0.55 : 1,
          width: '44px',
          height: '44px',
          borderRadius: '999px',
          border: primary ? '1px solid var(--text-primary)' : '1px solid var(--border-default)',
          background: primary ? 'var(--text-primary)' : 'var(--bg-secondary)',
          color: primary ? 'var(--bg-primary)' : 'var(--text-secondary)',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          padding: 0,
          cursor: disabled ? 'default' : 'pointer',
          boxShadow: primary ? 'var(--composer-action-shadow)' : 'none',
          transition: 'background-color 120ms ease, border-color 120ms ease, color 120ms ease, opacity 120ms ease'
        } as CSSProperties}
      >
        <Icon size={20} strokeWidth={2.2} aria-hidden="true" />
      </button>
    </ActionTooltip>
  )
}

function WelcomeStep({
  workspacePath,
  locale,
  switchingDisplayLocale,
  onDisplayLocaleChange,
  onChooseDifferentWorkspace
}: {
  workspacePath: string
  locale: AppLocale
  switchingDisplayLocale: boolean
  onDisplayLocaleChange(next: AppLocale): Promise<void>
  onChooseDifferentWorkspace(): void
}): JSX.Element {
  const t = useT()
  return (
    <div>
      <h1 style={{ margin: '0 0 10px', fontSize: '24px', fontWeight: 700 }}>
        {t('setupWizard.welcome.title')}
      </h1>
      <p style={{ margin: '0 0 14px', color: 'var(--text-secondary)', lineHeight: 1.6 }}>
        {t('setupWizard.welcome.description')}
      </p>
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: '12px',
          padding: '14px',
          borderRadius: '8px',
          border: '1px solid var(--border-default)',
          background: 'var(--bg-primary)',
          marginTop: '16px'
        }}
      >
        <Folder size={22} strokeWidth={1.8} aria-hidden="true" style={{ color: 'var(--accent)', flexShrink: 0 }} />
        <div style={{ minWidth: 0, flex: 1 }}>
          <div style={{ marginBottom: '5px', fontSize: '12px', fontWeight: 600, color: 'var(--text-primary)' }}>
            {t('setupWizard.workspacePath.label')}
          </div>
          <div
            style={{
              color: 'var(--text-secondary)',
              fontSize: '12px',
              fontFamily: 'var(--font-mono)',
              wordBreak: 'break-all'
            }}
          >
            {workspacePath}
          </div>
        </div>
        <Button
          size="sm"
          variant="secondary"
          onClick={onChooseDifferentWorkspace}
          style={{ flexShrink: 0 }}
        >
          {t('setupWizard.workspacePath.change')}
        </Button>
      </div>
      <div
        style={{
          marginTop: '16px',
          padding: '14px',
          borderRadius: '8px',
          border: '1px solid var(--border-default)',
          background: 'var(--bg-primary)'
        }}
      >
        <div style={{ marginBottom: '12px', fontSize: '13px', fontWeight: 700 }}>
          {t('setupWizard.initialPreferences.title')}
        </div>
        <label
          htmlFor="setup-display-language"
          style={{ display: 'block', marginBottom: '6px', fontSize: '12px', fontWeight: 600 }}
        >
          {t('setupWizard.welcome.language')}
        </label>
        <SettingsSelect<AppLocale>
          id="setup-display-language"
          value={locale}
          onValueChange={onDisplayLocaleChange}
          ariaLabel={t('setupWizard.welcome.language')}
          disabled={switchingDisplayLocale}
          style={{
            width: '220px',
            opacity: switchingDisplayLocale ? 0.7 : 1
          }}
          options={SUPPORTED_LOCALES.map((item) => ({
            value: item.value,
            label: item.nativeName
          }))}
        />
        <p style={{ margin: '12px 0 0', color: 'var(--text-dimmed)', fontSize: '13px', lineHeight: 1.55 }}>
          {t('setupWizard.welcome.note')}
        </p>
      </div>
    </div>
  )
}

function BootstrapImportStep({
  sources,
  selectedSourceId,
  onChange
}: {
  sources: WorkspaceSetupBootstrapImportSource[]
  selectedSourceId: WorkspaceSetupBootstrapImportSourceId | null
  onChange(sourceId: WorkspaceSetupBootstrapImportSourceId | null): void
}): JSX.Element {
  const t = useT()
  return (
    <div>
      <h1 style={{ margin: '0 0 10px', fontSize: '24px', fontWeight: 700 }}>
        {t('setupWizard.import.title')}
      </h1>
      <p style={{ margin: '0 0 16px', color: 'var(--text-secondary)', lineHeight: 1.6 }}>
        {t('setupWizard.import.description')}
      </p>
      <div role="radiogroup" aria-label={t('setupWizard.import.title')} style={{ display: 'grid', gap: '10px' }}>
        <button
          type="button"
          role="radio"
          aria-checked={selectedSourceId == null}
          onClick={() => {
            onChange(null)
          }}
          style={{
            ...cardStyle(selectedSourceId == null),
            display: 'grid',
            gridTemplateColumns: 'auto minmax(0, 1fr)',
            alignItems: 'center',
            gap: '12px',
            textAlign: 'left'
          }}
        >
          <Folder size={28} strokeWidth={1.8} aria-hidden="true" />
          <div style={{ minWidth: 0 }}>
            <div style={{ fontSize: '14px', fontWeight: 600, color: 'var(--text-primary)' }}>
              {t('setupWizard.import.none.title')}
            </div>
            <div style={{ marginTop: '6px', fontSize: '13px', lineHeight: 1.55, color: 'var(--text-secondary)' }}>
              {t('setupWizard.import.none.description')}
            </div>
          </div>
        </button>

        {sources.map((source) => {
          const active = selectedSourceId === source.id
          return (
            <button
              key={source.id}
              type="button"
              role="radio"
              aria-checked={active}
              onClick={() => {
                onChange(source.id)
              }}
              style={{
                ...cardStyle(active),
                display: 'grid',
                gridTemplateColumns: 'auto minmax(0, 1fr)',
                alignItems: 'center',
                gap: '12px',
                textAlign: 'left'
              }}
            >
              <BootstrapImportSourceIcon source={source.id} size={30} />
              <div style={{ minWidth: 0 }}>
                <div style={{ fontSize: '14px', fontWeight: 600, color: 'var(--text-primary)' }}>
                  {t(`setupWizard.import.source.${source.id}.title`)}
                </div>
                <div style={{ marginTop: '6px', fontSize: '13px', lineHeight: 1.55, color: 'var(--text-secondary)' }}>
                  {t(`setupWizard.import.source.${source.id}.description`, {
                    file: source.fileName
                  })}
                </div>
                <ActionTooltip
                  label={source.path}
                  wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flexShrink: 1, marginTop: '8px' }}
                >
                  <div
                    style={{
                      fontFamily: 'var(--font-mono)',
                      fontSize: '12px',
                      color: 'var(--text-dimmed)',
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                      whiteSpace: 'nowrap',
                      display: 'block'
                    }}
                  >
                    {source.relativePath}
                  </div>
                </ActionTooltip>
              </div>
            </button>
          )
        })}
      </div>
      <p style={{ margin: '14px 0 0', fontSize: '13px', lineHeight: 1.55, color: 'var(--text-secondary)' }}>
        {selectedSourceId
          ? t('setupWizard.import.selectedNote')
          : t('setupWizard.import.skippedNote')}
      </p>
    </div>
  )
}

function ExistingProviderForm({
  providers,
  selectedProviderId,
  selectedProvider,
  signInState,
  loginError,
  onLoginChatGpt,
  onChange
}: {
  providers: WorkspaceSetupProviderSummary[]
  selectedProviderId: string
  selectedProvider: WorkspaceSetupProviderSummary | null
  signInState: ChatGptSignInState
  loginError: string | null
  onLoginChatGpt(): void
  onChange(providerId: string): void
}): JSX.Element {
  const t = useT()
  // A saved subscription provider has no auth-method cards, so its sign-in lives on the provider row.
  const needsSignIn = selectedProvider?.authMethod === 'chatgptOAuth' && signInState !== 'signedIn'
  return (
    <div style={{ display: 'grid', gap: '14px' }}>
      <div>
        <label htmlFor="setup-existing-provider" style={{ display: 'block', marginBottom: '6px', fontSize: '12px', fontWeight: 600 }}>
          {t('setupWizard.field.provider')}
        </label>
        <SettingsSelect
          id="setup-existing-provider"
          value={selectedProviderId || providers[0]?.id || ''}
          onValueChange={onChange}
          ariaLabel={t('setupWizard.field.provider')}
          options={providers.map((provider) => ({
            value: provider.id,
            label: provider.displayName
          }))}
        />
      </div>
      {needsSignIn && (
        <div>
          <div className="setup-auth-card">
            <span className="setup-auth-choice">
              <span className="setup-auth-title">{t('setupWizard.authMethod.chatgpt')}</span>
            </span>
            <span className="setup-auth-action">
              <ChatGptSignInAction state={signInState} onSignIn={onLoginChatGpt} />
            </span>
          </div>
          {loginError && (
            <div className="setup-auth-error" role="alert">
              {t('settings.llm.authMethod.signInFailed', { error: loginError })}
            </div>
          )}
        </div>
      )}
    </div>
  )
}

function TemplateProviderForm({
  draft,
  signInState,
  loginError,
  onLoginChatGpt,
  onChange
}: {
  draft: WorkspaceSetupProviderDraft
  signInState: ChatGptSignInState
  loginError: string | null
  onLoginChatGpt(): void
  onChange(partial: Partial<WorkspaceSetupProviderDraft>): void
}): JSX.Element {
  const t = useT()
  // ChatGPT OAuth only works on the Responses path; chat-completions providers must keep the
  // API-key flow even when the user previously picked the OAuth card on another protocol.
  const isOpenAiResponses = draft.protocol === OPENAI_RESPONSES_PROTOCOL
  const authMethod = draft.authMethod ?? 'apiKey'
  const oauthMode = isOpenAiResponses && authMethod === 'chatgptOAuth'

  return (
    <div style={{ display: 'grid', gap: '14px' }}>
      {isOpenAiResponses && (
        <div>
          <label style={{ display: 'block', marginBottom: '6px', fontSize: '12px', fontWeight: 600 }}>
            {t('setupWizard.field.authMethod')}
          </label>
          <div className="setup-auth-group" role="radiogroup" aria-label={t('setupWizard.field.authMethod')}>
            <AuthMethodCard
              value="apiKey"
              active={authMethod === 'apiKey'}
              title={t('setupWizard.authMethod.apiKey')}
              description={t('setupWizard.authMethod.apiKeyDescription')}
              onSelect={() => onChange({ authMethod: 'apiKey' })}
            />
            <AuthMethodCard
              value="chatgptOAuth"
              active={authMethod === 'chatgptOAuth'}
              title={t('setupWizard.authMethod.chatgpt')}
              description={t('setupWizard.authMethod.chatgptDescription')}
              onSelect={() => onChange({ authMethod: 'chatgptOAuth', apiKey: '' })}
              action={oauthMode ? <ChatGptSignInAction state={signInState} onSignIn={onLoginChatGpt} /> : undefined}
            />
          </div>
          {oauthMode && loginError && (
            <div className="setup-auth-error" role="alert">
              {t('settings.llm.authMethod.signInFailed', { error: loginError })}
            </div>
          )}
        </div>
      )}

      {!oauthMode && (
        <>
          <div>
            <label style={{ display: 'block', marginBottom: '6px', fontSize: '12px', fontWeight: 600 }}>
              {t('setupWizard.field.apiKey')}
            </label>
            <SecretInput
              value={draft.apiKey}
              onChange={(apiKey) => onChange({ apiKey })}
              placeholder={t('setupWizard.placeholder.apiKey')}
              mono
            />
          </div>
          <div>
            <label htmlFor="setup-template-endpoint" style={{ display: 'block', marginBottom: '6px', fontSize: '12px', fontWeight: 600 }}>
              {t('setupWizard.field.endpoint')}
            </label>
            <Input
              id="setup-template-endpoint"
              value={draft.endPoint}
              onChange={(e) => onChange({ endPoint: e.target.value })}
              placeholder={defaultProviderEndpoint(draft.protocol)}
            />
          </div>
        </>
      )}
    </div>
  )
}

type ChatGptSignInState = 'required' | 'pending' | 'signedIn'

function ChatGptSignInAction({ state, onSignIn }: { state: ChatGptSignInState; onSignIn(): void }): JSX.Element {
  const t = useT()
  if (state === 'signedIn') {
    return (
      <span className="setup-auth-state">
        <Check size={14} aria-hidden="true" />
        {t('setupWizard.authMethod.signedIn')}
      </span>
    )
  }
  return (
    <Button variant="secondary" size="sm" loading={state === 'pending'} onClick={onSignIn}>
      {t('setupWizard.authMethod.signIn')}
    </Button>
  )
}

// The action sits beside the label rather than inside it, so clicking it cannot also toggle the radio.
function AuthMethodCard({
  value,
  active,
  title,
  description,
  onSelect,
  action
}: {
  value: NonNullable<WorkspaceSetupProviderDraft['authMethod']>
  active: boolean
  title: string
  description: string
  onSelect: () => void
  action?: ReactNode
}): JSX.Element {
  return (
    <div className="setup-auth-card" data-active={active}>
      <label className="setup-auth-choice">
        <input
          type="radio"
          name="setup-auth-method"
          value={value}
          checked={active}
          onChange={onSelect}
        />
        <span className="setup-auth-title">{title}</span>
        <span className="setup-auth-description">{description}</span>
      </label>
      {action ? <span className="setup-auth-action">{action}</span> : null}
    </div>
  )
}

function CustomProviderForm({
  draft,
  timeoutDraft,
  signInState,
  loginError,
  onLoginChatGpt,
  onChange,
  onTimeoutChange
}: {
  draft: WorkspaceSetupProviderDraft
  timeoutDraft: string
  signInState: ChatGptSignInState
  loginError: string | null
  onLoginChatGpt(): void
  onChange(partial: Partial<WorkspaceSetupProviderDraft>): void
  onTimeoutChange(value: string): void
}): JSX.Element {
  const t = useT()
  return (
    <div style={{ display: 'grid', gap: '14px' }}>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '14px' }}>
        <div>
          <label htmlFor="setup-provider-id" style={{ display: 'block', marginBottom: '6px', fontSize: '12px', fontWeight: 600 }}>
            {t('setupWizard.field.providerId')}
          </label>
          <Input
            id="setup-provider-id"
            value={draft.id}
            onChange={(e) => onChange({ id: slugProviderId(e.target.value) })}
            placeholder="provider"
          />
        </div>
        <div>
          <label htmlFor="setup-provider-display-name" style={{ display: 'block', marginBottom: '6px', fontSize: '12px', fontWeight: 600 }}>
            {t('setupWizard.field.displayName')}
          </label>
          <Input
            id="setup-provider-display-name"
            value={draft.displayName}
            onChange={(e) => onChange({ displayName: e.target.value })}
            placeholder={t('setupWizard.placeholder.displayName')}
          />
        </div>
      </div>
      <div>
        <label htmlFor="setup-provider-protocol" style={{ display: 'block', marginBottom: '6px', fontSize: '12px', fontWeight: 600 }}>
          {t('setupWizard.field.protocol')}
        </label>
        <SettingsSelect<WorkspaceSetupProviderProtocol>
          id="setup-provider-protocol"
          value={draft.protocol}
          onValueChange={(protocol) => {
            // ChatGPT OAuth is Responses-only; reset the auth method when leaving that protocol
            // so the connection section reverts to API-key fields and the saved payload stays valid.
            const partial: Partial<WorkspaceSetupProviderDraft> = {
              protocol,
              endPoint: defaultProviderEndpoint(protocol)
            }
            if (protocol !== OPENAI_RESPONSES_PROTOCOL && draft.authMethod === 'chatgptOAuth') {
              partial.authMethod = 'apiKey'
            }
            onChange(partial)
          }}
          ariaLabel={t('setupWizard.field.protocol')}
          options={DESKTOP_PROVIDER_PROTOCOLS.map((protocol) => ({
            value: protocol,
            label: providerProtocolLabel(protocol)
          }))}
        />
      </div>
      <TemplateProviderForm
        draft={draft}
        signInState={signInState}
        loginError={loginError}
        onLoginChatGpt={onLoginChatGpt}
        onChange={onChange}
      />
      <div>
        <label htmlFor="setup-provider-timeout" style={{ display: 'block', marginBottom: '6px', fontSize: '12px', fontWeight: 600 }}>
          {t('setupWizard.field.timeout')}
        </label>
        <Input
          id="setup-provider-timeout"
          type="number"
          className="dc-plain-number"
          min={1}
          value={timeoutDraft}
          onChange={(e) => onTimeoutChange(e.target.value)}
          placeholder="600"
        />
      </div>
    </div>
  )
}

function ModelField({
  preference,
  models,
  modelListLoading,
  modelSelectAvailable,
  modelLoadState,
  onRetry,
  onChange
}: {
  preference: ModelPreference
  models: ModelCatalogItem[]
  modelListLoading: boolean
  modelSelectAvailable: boolean
  modelLoadState: 'idle' | 'loading' | 'ready' | 'auth-required' | 'unsupported' | 'missing-key' | 'error'
  onRetry(): void
  onChange(preference: ModelPreference): void
}): JSX.Element {
  const t = useT()
  return (
    <div style={{ marginTop: '14px' }}>
      <label htmlFor="setup-model" style={{ display: 'block', marginBottom: '6px', fontSize: '12px', fontWeight: 600 }}>
        {t('setupWizard.field.model')}
      </label>
      <PreferenceModelPicker
        preference={preference}
        models={models}
        loading={modelListLoading}
        disabled={false}
        errorMessage={modelLoadState === 'error' ? t('setupWizard.modelListUnavailable') : null}
        manualFallback={!modelListLoading && !modelSelectAvailable}
        onRetry={onRetry}
        onChange={onChange}
        inputId="setup-model"
        inputAriaLabel={t('setupWizard.field.model')}
        placeholder={t('setupWizard.placeholder.model')}
      />
      {(modelLoadState === 'unsupported' || modelLoadState === 'missing-key' || modelLoadState === 'error') && (
        <div style={{ marginTop: '6px', fontSize: '12px', color: 'var(--text-dimmed)' }}>
          {t('setupWizard.modelListUnavailable')}
        </div>
      )}
      {modelLoadState === 'error' && (
        <Button size="sm" variant="secondary" onClick={onRetry} style={{ marginTop: '8px' }}>
          {t('common.retry')}
        </Button>
      )}
    </div>
  )
}

function ConfirmStep({
  displayLanguage,
  providerName,
  providerId,
  protocol,
  preference,
  setAsUserDefault,
  bootstrapImportSource,
  submitError,
  submitWarning
}: {
  displayLanguage: string
  providerName: string
  providerId: string
  protocol: WorkspaceSetupProviderProtocol
  preference: ModelPreference
  setAsUserDefault: boolean
  bootstrapImportSource: WorkspaceSetupBootstrapImportSource | null
  submitError: string | null
  submitWarning: string | null
}): JSX.Element {
  const t = useT()
  return (
    <div>
      <h1 style={{ margin: '0 0 10px', fontSize: '24px', fontWeight: 700 }}>
        {t('setupWizard.confirm.title')}
      </h1>
      <p style={{ margin: '0 0 18px', color: 'var(--text-secondary)', lineHeight: 1.6 }}>
        {t('setupWizard.confirm.description')}
      </p>

      <div
        style={{
          display: 'grid',
          gap: '10px',
          marginBottom: '16px',
          padding: '14px',
          borderRadius: '8px',
          border: '1px solid var(--border-default)',
          background: 'var(--bg-primary)',
          fontSize: '13px'
        }}
      >
        <div className="setup-summary-provider">
          <ProviderProtocolIcon protocol={protocol} size={30} />
          <div className="setup-summary-provider-body">
            <div className="setup-summary-provider-name">{providerName}</div>
            <div className="setup-summary-provider-meta">
              <span className="setup-summary-provider-id">{providerId}</span>
              <span aria-hidden>·</span>
              <span>{providerProtocolLabel(protocol)}</span>
            </div>
            <ProviderModelSummary main={preference} mainLabel={t('setupWizard.summary.model')} />
          </div>
        </div>
        <SummaryRow label={t('setupWizard.summary.displayLanguage')} value={displayLanguage} />
        <SummaryRow
          label={t('setupWizard.summary.userDefault')}
          value={setAsUserDefault ? t('setupWizard.summary.yes') : t('setupWizard.summary.no')}
        />
        <SummaryRow
          label={t('setupWizard.summary.import')}
          value={bootstrapImportSource
            ? t('setupWizard.summary.importSource', {
              source: t(`setupWizard.import.source.${bootstrapImportSource.id}.title`),
              file: bootstrapImportSource.relativePath
            })
            : t('setupWizard.summary.importNone')}
        />
      </div>

      {submitWarning && (
        <div
          style={{
            marginTop: '14px',
            padding: '12px 14px',
            borderRadius: '8px',
            border: '1px solid color-mix(in srgb, var(--warning) 42%, var(--border-default))',
            background: 'color-mix(in srgb, var(--warning) 10%, transparent)',
            color: 'var(--text-primary)',
            fontSize: '13px',
            whiteSpace: 'pre-wrap',
            wordBreak: 'break-word'
          }}
        >
          {submitWarning}
        </div>
      )}

      {submitError && (
        <div
          style={{
            marginTop: '14px',
            padding: '12px 14px',
            borderRadius: '8px',
            border: '1px solid rgba(239, 68, 68, 0.35)',
            background: 'rgba(239, 68, 68, 0.08)',
            color: 'var(--error)',
            fontSize: '13px',
            whiteSpace: 'pre-wrap',
            wordBreak: 'break-word'
          }}
        >
          {submitError}
        </div>
      )}
    </div>
  )
}

function SummaryRow({
  label,
  value
}: {
  label: string
  value: string
}): JSX.Element {
  return (
    <div
      style={{
        display: 'grid',
        gridTemplateColumns: '140px minmax(0, 1fr)',
        gap: '12px',
        alignItems: 'start'
      }}
    >
      <div style={{ color: 'var(--text-dimmed)' }}>{label}</div>
      <div
        style={{
          color: 'var(--text-primary)',
          wordBreak: 'break-word'
        }}
      >
        {value}
      </div>
    </div>
  )
}
