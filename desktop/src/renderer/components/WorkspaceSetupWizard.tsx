import { useCallback, useEffect, useMemo, useRef, useState, type MutableRefObject, type Ref } from 'react'
import { AlertCircle, Check, FileText, TriangleAlert } from 'lucide-react'
import { normalizeLocale, SUPPORTED_LOCALES, type AppLocale } from '../../shared/locales'
import { cloneModelPreference, createManualModelPreference, type ModelPreference } from '../../shared/modelPreference'
import type {
  WorkspaceSetupBootstrapImportSourceId,
  WorkspaceSetupModelListRequest,
  WorkspaceSetupRequest,
  WorkspaceSetupRunResult,
  WorkspaceStatusPayload
} from '../../preload/api.d'
import { useLocale, useSetUiLocale, useT } from '../contexts/LocaleContext'
import { parseModelCatalogItems, type ModelCatalogItem } from '../stores/modelCatalogStore'
import { createCatalogDefaultPreference, normalizePreferenceForModel } from './conversation/PreferenceModelPicker'
import { ActionTooltip } from './ui/ActionTooltip'
import { Button } from './ui/Button'
import { RunningShimmer } from './ui/RunningShimmer'
import { BootstrapImportSourceIcon } from './setup/BootstrapImportSourceIcon'
import {
  connectionValid,
  finalDraft,
  initialConnections,
  SetupAccessSection,
  SetupChoiceRow,
  type SetupAccessChoice,
  type SetupConnection,
  type SetupConnectionKind,
  type SetupConnectionPatch,
  type SetupSignInState
} from './setup/SetupAccessSection'
import { SetupModelSection, type SetupModelCatalog } from './setup/SetupModelSection'
import { SetupWorkspaceSection } from './setup/SetupWorkspaceSection'
import { centeredLaunchLogoRect, elementToLaunchLogoRect, type LaunchLogoRect } from './WorkspaceLaunchTransition'

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

type SectionId = 'workspace' | 'instructions' | 'access' | 'model'
type SubmitState = { phase: 'idle' } | { phase: 'submitting' } | { phase: 'error'; message: string } | { phase: 'warning' }

const setupLogoUrl = new URL('../../../resources/dotcraft.svg', import.meta.url).toString()

function folderName(path: string): string {
  const parts = path.split(/[\\/]+/).filter(Boolean)
  return parts[parts.length - 1] ?? path
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
  const logoNode = useRef<HTMLDivElement | null>(null)
  const setLogoNode = useCallback((node: HTMLDivElement | null): void => {
    logoNode.current = node
    if (typeof logoAnchorRef === 'function') logoAnchorRef(node)
    else if (logoAnchorRef) (logoAnchorRef as MutableRefObject<HTMLDivElement | null>).current = node
  }, [logoAnchorRef])
  const providers = workspaceStatus.providers ?? []
  const defaults = workspaceStatus.userConfigDefaults
  const importSources = workspaceStatus.bootstrapImportSources ?? []
  const sections = useMemo<SectionId[]>(
    () => importSources.length > 0 ? ['workspace', 'instructions', 'access', 'model'] : ['workspace', 'access', 'model'],
    [importSources.length]
  )

  const [section, setSection] = useState<SectionId>('workspace')
  const [visited, setVisited] = useState<ReadonlySet<SectionId>>(() => new Set(['workspace']))
  const [importSourceId, setImportSourceId] = useState<WorkspaceSetupBootstrapImportSourceId | null>(importSources[0]?.id ?? null)
  const defaultSaved = providers.find((provider) => provider.id === defaults?.providerId?.trim()) ?? providers[0]
  const [access, setAccess] = useState<SetupAccessChoice>(defaultSaved ? `saved:${defaultSaved.id}` : 'chatgpt')
  const [showMore, setShowMore] = useState(providers.length === 0)
  const [connections, setConnections] = useState<Record<SetupConnectionKind, SetupConnection>>(() => initialConnections(providers))
  const [signIn, setSignIn] = useState<SetupSignInState>({ phase: 'idle' })
  const [savedSignIn, setSavedSignIn] = useState<SetupSignInState>({ phase: 'idle' })
  const [catalog, setCatalog] = useState<SetupModelCatalog | null>(null)
  const [catalogNonce, setCatalogNonce] = useState(0)
  const [preference, setPreference] = useState<ModelPreference>(() =>
    defaults?.preference ? cloneModelPreference(defaults.preference) : createManualModelPreference(defaults?.model?.trim() || '')
  )
  const [defaultOverride, setDefaultOverride] = useState<boolean | null>(null)
  const [submit, setSubmit] = useState<SubmitState>({ phase: 'idle' })
  const requestGeneration = useRef(0)

  const savedProvider = access.startsWith('saved:') ? providers.find((provider) => `saved:${provider.id}` === access) ?? null : null
  const newKind: SetupConnectionKind | null = savedProvider ? null : (access as SetupConnectionKind)
  const accessValid = savedProvider != null || (newKind != null && connectionValid(newKind, connections[newKind], signIn))
  const listRequest = useMemo<WorkspaceSetupModelListRequest | null>(() => {
    if (savedProvider) return { providerId: savedProvider.id }
    if (newKind && accessValid) return { provider: finalDraft(newKind, connections[newKind], providers) }
    return null
  }, [accessValid, connections, newKind, providers, savedProvider])
  const listKey = listRequest ? `${JSON.stringify(listRequest)}#${catalogNonce}` : ''
  const setAsUserDefault = defaultOverride ?? savedProvider == null
  const modelReady = catalog?.key === listKey && catalog.status !== 'loading'
  const modelValid = preference.model.trim().length > 0 && modelReady

  const validity: Record<SectionId, boolean> = {
    workspace: true,
    instructions: true,
    access: accessValid,
    model: accessValid && modelValid
  }
  const sectionIndex = sections.indexOf(section)
  const isLast = sectionIndex === sections.length - 1
  const reachable = (id: SectionId): boolean => sections.slice(0, sections.indexOf(id)).every((item) => validity[item])

  useEffect(() => {
    if (section !== 'model' || !listRequest || catalog?.key === listKey) return
    const generation = ++requestGeneration.current
    setCatalog({ key: listKey, status: 'loading', models: [] })
    void window.api.workspace.listSetupModels(listRequest)
      .then((result) => {
        if (generation !== requestGeneration.current) return
        if (result.kind !== 'success') {
          setCatalog({ key: listKey, status: result.kind, models: [] })
          return
        }
        const parsed = new Map(parseModelCatalogItems({ success: true, models: result.models }).map((item) => [item.id, item]))
        const models = result.models.map((item) => parsed.get(item.id)).filter((item): item is ModelCatalogItem => item != null)
        setCatalog({ key: listKey, status: 'ready', models })
        setPreference((current) => {
          if (models.some((item) => item.id === current.model)) return normalizePreferenceForModel(current, models)
          const remembered = defaults?.preference ?? (defaults?.model ? createManualModelPreference(defaults.model) : null)
          if (remembered && models.some((item) => item.id === remembered.model)) return normalizePreferenceForModel(remembered, models)
          return models.length > 0 ? createCatalogDefaultPreference(models[0], models[0].id) : current
        })
      })
      .catch(() => {
        if (generation === requestGeneration.current) setCatalog({ key: listKey, status: 'error', models: [] })
      })
  }, [catalog?.key, defaults?.model, defaults?.preference, listKey, listRequest, section])

  const goTo = useCallback((id: SectionId) => {
    setSection(id)
    setVisited((current) => new Set([...current, ...sections.slice(0, sections.indexOf(id) + 1)]))
  }, [sections])

  function updateConnection(kind: SetupConnectionKind, partial: SetupConnectionPatch): void {
    setConnections((current) => ({
      ...current,
      [kind]: {
        ...current[kind],
        ...partial,
        draft: { ...current[kind].draft, ...(partial.draft ?? {}) }
      }
    }))
  }

  async function loginChatGpt(providerId: string, setState: (state: SetupSignInState) => void): Promise<boolean> {
    setState({ phase: 'pending' })
    try {
      const result = await window.api.workspace.loginSetupChatGpt(providerId)
      if (result.kind === 'success') {
        setState({ phase: 'signedIn' })
        return true
      }
      setState({ phase: 'failed', message: result.errorMessage || result.errorCode || 'Unknown error' })
    } catch (error) {
      setState({ phase: 'failed', message: error instanceof Error ? error.message : String(error) })
    }
    return false
  }

  async function signInNewConnection(): Promise<void> {
    if (signIn.phase === 'pending') return
    if (await loginChatGpt(connections.chatgpt.draft.id, setSignIn)) goTo('model')
  }

  async function signInSavedProvider(): Promise<void> {
    if (!savedProvider || savedSignIn.phase === 'pending') return
    if (await loginChatGpt(savedProvider.id, setSavedSignIn)) setCatalogNonce((value) => value + 1)
  }

  async function changeLocale(next: AppLocale): Promise<void> {
    const normalized = normalizeLocale(next)
    if (normalized === locale) return
    setUiLocale(normalized)
    try {
      await window.api.settings.set({ locale: normalized })
    } catch {
      return
    }
  }

  function buildRequest(): WorkspaceSetupRequest {
    const base = {
      model: preference.model.trim(),
      preference: cloneModelPreference(preference),
      setAsUserDefault,
      ...(importSourceId ? { bootstrapImportSourceId: importSourceId } : {})
    }
    if (savedProvider) return { ...base, providerMode: 'existing', providerId: savedProvider.id }
    const kind = newKind ?? 'chatgpt'
    return { ...base, providerMode: 'create', provider: finalDraft(kind, connections[kind], providers) }
  }

  async function create(): Promise<void> {
    if (submit.phase === 'submitting' || !validity.model) return
    const runSetup = onRunSetup ?? ((request: WorkspaceSetupRequest) => window.api.workspace.runSetup(request))
    setSubmit({ phase: 'submitting' })
    try {
      const result = await runSetup(buildRequest(), {
        logoRect: elementToLaunchLogoRect(logoNode.current) ?? centeredLaunchLogoRect(),
        logoSrc: setupLogoUrl
      })
      setSubmit(result && typeof result === 'object' && result.bootstrapImport?.warning ? { phase: 'warning' } : { phase: 'idle' })
    } catch (error) {
      setSubmit({ phase: 'error', message: error instanceof Error ? error.message : String(error) })
    }
  }

  function back(): void {
    if (submit.phase === 'submitting') return
    if (sectionIndex === 0) {
      onCancel()
      return
    }
    goTo(sections[sectionIndex - 1])
  }

  const needsChatGptSignIn = section === 'access' && newKind === 'chatgpt' && signIn.phase !== 'signedIn'
  const primary = isLast
    ? {
        label: submit.phase === 'submitting' ? t('setupWizard.action.creating') : t('setupWizard.action.create'),
        disabled: !validity.model,
        loading: submit.phase === 'submitting',
        run: () => { void create() }
      }
    : needsChatGptSignIn
      ? { label: t('setupWizard.action.signIn'), disabled: false, loading: signIn.phase === 'pending', run: () => { void signInNewConnection() } }
      : { label: t('setupWizard.action.continue'), disabled: !validity[section], loading: false, run: () => goTo(sections[sectionIndex + 1]) }

  const languageName = SUPPORTED_LOCALES.find((item) => item.value === locale)?.nativeName ?? locale
  const accessValue = savedProvider
    ? savedProvider.displayName
    : newKind === 'chatgpt'
      ? signIn.phase === 'signedIn' ? t('setupWizard.value.chatgpt') : null
      : accessValid && newKind
        ? newKind === 'other'
          ? connections.other.draft.displayName.trim() || t('setupWizard.value.other')
          : t(newKind === 'openai' ? 'setupWizard.value.openai' : 'setupWizard.value.anthropic')
        : null
  const selectedModel = catalog?.models.find((item) => item.id === preference.model) ?? null
  const reasoningLabel = selectedModel?.reasoning
    ? preference.reasoning.enabled ? t(`composer.reasoning.${preference.reasoning.effort}`) : t('composer.reasoning.off')
    : null
  const modelValue = preference.model.trim() ? [preference.model.trim(), reasoningLabel].filter(Boolean).join(' · ') : null
  const outlineValues: Record<SectionId, string | null> = {
    workspace: languageName,
    instructions: importSourceId ? t('setupWizard.value.import') : t('setupWizard.value.noImport'),
    access: accessValue,
    model: accessValid ? modelValue : null
  }

  return (
    <div className="workspace-setup" data-handoff={deferContent ? 'true' : undefined}>
      <aside className="workspace-setup__rail">
        <div className="workspace-setup__identity">
          <div ref={setLogoNode} className="workspace-setup__logo" data-hidden={hideLogo ? 'true' : undefined} aria-hidden="true">
            <img src={setupLogoUrl} alt="" width={48} height={48} draggable={false} />
          </div>
          <RunningShimmer as="div" className="workspace-setup__kicker">{t('setupWizard.kicker')}</RunningShimmer>
          <ActionTooltip label={workspacePath} placement="bottom" wrapperStyle={{ display: 'block', minWidth: 0 }}>
            <div className="workspace-setup__folder">{folderName(workspacePath)}</div>
          </ActionTooltip>
        </div>
        <nav className="workspace-setup__outline" aria-label={t('setupWizard.outlineLabel')}>
          <ol>
            {sections.map((id) => {
              const current = id === section
              const canOpen = reachable(id) && submit.phase !== 'submitting'
              const state = current ? 'current' : visited.has(id) && validity[id] ? 'done' : 'upcoming'
              const value = outlineValues[id]
              return (
                <li key={id}>
                  <button
                    type="button"
                    className="workspace-setup__step"
                    data-state={state}
                    aria-current={current ? 'step' : undefined}
                    disabled={!canOpen}
                    onClick={() => goTo(id)}
                  >
                    <span className="workspace-setup__marker" aria-hidden="true">
                      {state === 'done' ? <Check size={11} strokeWidth={3} /> : null}
                    </span>
                    <span className="workspace-setup__step-text">
                      <span className="workspace-setup__step-label">{t(`setupWizard.outline.${id}`)}</span>
                      <span className="workspace-setup__step-value" data-empty={value ? undefined : 'true'}>
                        {value ?? t('setupWizard.value.notSet')}
                      </span>
                    </span>
                  </button>
                </li>
              )
            })}
          </ol>
        </nav>
      </aside>

      <main className="workspace-setup__main">
        <header className="workspace-setup__header">
          <div className="workspace-setup__column">
            <h1 className="workspace-setup__title">{t(`setupWizard.${section}.title`)}</h1>
            <p className="workspace-setup__description">{t(`setupWizard.${section}.description`)}</p>
          </div>
        </header>

        <div className="workspace-setup__scroll">
          <div className="workspace-setup__column workspace-setup__content" key={section}>
            {section === 'workspace' && (
              <SetupWorkspaceSection
                path={workspacePath}
                locale={locale}
                onChangeFolder={onChooseDifferentWorkspace}
                onLocale={(next) => { void changeLocale(next) }}
              />
            )}
            {section === 'instructions' && (
              <div className="workspace-setup__group" role="radiogroup" aria-label={t('setupWizard.outline.instructions')}>
                {importSources.map((source) => (
                  <SetupChoiceRow
                    key={source.id}
                    selected={importSourceId === source.id}
                    mark={<BootstrapImportSourceIcon source={source.id} size={18} />}
                    title={t('setupWizard.instructions.import')}
                    hint={t('setupWizard.instructions.importHint', { file: source.relativePath })}
                    onSelect={() => setImportSourceId(source.id)}
                  />
                ))}
                <SetupChoiceRow
                  selected={importSourceId == null}
                  mark={<span className="workspace-setup__mark"><FileText size={16} strokeWidth={1.8} /></span>}
                  title={t('setupWizard.instructions.skip')}
                  hint={t('setupWizard.instructions.skipHint')}
                  onSelect={() => setImportSourceId(null)}
                />
              </div>
            )}
            {section === 'access' && (
              <SetupAccessSection
                providers={providers}
                defaultProviderId={defaultSaved?.id ?? null}
                access={access}
                showMore={showMore || newKind != null}
                connections={connections}
                signIn={signIn}
                onShowMore={() => setShowMore(true)}
                onSelect={setAccess}
                onUpdate={updateConnection}
              />
            )}
            {section === 'model' && (
              <SetupModelSection
                providerName={savedProvider?.displayName ?? accessValue ?? ''}
                catalog={catalog?.key === listKey ? catalog : { key: listKey, status: 'loading', models: [] }}
                preference={preference}
                setAsUserDefault={setAsUserDefault}
                savedNeedsSignIn={savedProvider?.authMethod === 'chatgptOAuth'}
                savedSignIn={savedSignIn}
                onSignIn={() => { void signInSavedProvider() }}
                onRetry={() => setCatalogNonce((value) => value + 1)}
                onPreference={setPreference}
                onDefault={setDefaultOverride}
              />
            )}
          </div>
        </div>

        <footer className="workspace-setup__footer">
          <div className="workspace-setup__column">
            {submit.phase === 'error' && (
              <div className="workspace-setup__notice" data-level="error" role="alert">
                <AlertCircle size={18} strokeWidth={1.9} aria-hidden="true" />
                <div>
                  <div className="workspace-setup__notice-title">{t('setupWizard.error.title')}</div>
                  <div className="workspace-setup__notice-body">{submit.message}</div>
                </div>
              </div>
            )}
            {submit.phase === 'warning' && (
              <div className="workspace-setup__notice" data-level="warning" role="status">
                <TriangleAlert size={18} strokeWidth={1.9} aria-hidden="true" />
                <div>
                  <div className="workspace-setup__notice-title">{t('setupWizard.warning.title')}</div>
                  <div className="workspace-setup__notice-body">{t('setupWizard.warning.body')}</div>
                </div>
              </div>
            )}
            <div className="workspace-setup__actions">
              <Button variant="ghost" disabled={submit.phase === 'submitting'} onClick={back}>
                {sectionIndex === 0 ? t('setupWizard.action.cancel') : t('setupWizard.action.back')}
              </Button>
              <Button
                variant="primary"
                className="workspace-setup__primary"
                disabled={primary.disabled}
                loading={primary.loading}
                onClick={primary.run}
              >
                {primary.label}
              </Button>
            </div>
          </div>
        </footer>
      </main>
    </div>
  )
}
