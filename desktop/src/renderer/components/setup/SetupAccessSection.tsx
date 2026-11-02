import { useEffect, useRef, type ReactNode } from 'react'
import { Check } from 'lucide-react'
import type {
  WorkspaceSetupProviderDraft,
  WorkspaceSetupProviderSummary
} from '../../../preload/api.d'
import type { DesktopProviderProtocol } from '../../../shared/providerProtocols'
import { useT } from '../../contexts/LocaleContext'
import { slugProviderId, uniqueProviderId } from '../../utils/providerId'
import { SecretInput } from '../channels/FormShared'
import { ProviderPresetMark } from '../settings/providers/ProviderPresetMark'
import {
  apiFormatOptions,
  findProviderPreset,
  presetConnection,
  providerPresetAuth,
  providerPresetIdFor,
  type ProviderPreset,
  type ProviderPresetId
} from '../settings/providers/providerPresets'
import { SegmentedControl } from '../settings/ui/SegmentedControl'
import { SettingsSelect } from '../settings/ui/SettingsSelect'
import { DisclosureButton } from '../ui/DisclosureButton'
import { Input } from '../ui/Input'

export type SetupAccessChoice = ProviderPresetId | `saved:${string}`
export type SetupSignInState = { phase: 'idle' } | { phase: 'pending' } | { phase: 'signedIn' } | { phase: 'failed'; message: string }
export interface SetupConnection { draft: WorkspaceSetupProviderDraft; timeout: string; idEdited: boolean; advanced: boolean }
export type SetupConnectionPatch = Partial<Omit<SetupConnection, 'draft'>> & { draft?: Partial<WorkspaceSetupProviderDraft> }
export type SetupConnections = Partial<Record<ProviderPresetId, SetupConnection>>

function isHttpUrl(value: string): boolean {
  try {
    const parsed = new URL(value.trim())
    return parsed.protocol === 'http:' || parsed.protocol === 'https:'
  } catch {
    return false
  }
}

function timeoutValid(value: string): boolean {
  if (!value.trim()) return true
  const parsed = Number(value.trim())
  return Number.isFinite(parsed) && parsed > 0
}

function derivedProviderId(connection: SetupConnection, providers: WorkspaceSetupProviderSummary[]): string {
  return uniqueProviderId(connection.draft.displayName || 'provider', providers)
}

export function isSavedChoice(choice: SetupAccessChoice): choice is `saved:${string}` {
  return choice.startsWith('saved:')
}

export function initialConnections(
  presets: readonly ProviderPreset[],
  providers: WorkspaceSetupProviderSummary[]
): SetupConnections {
  return Object.fromEntries(presets.map((preset) => {
    const connection = presetConnection(preset)
    const draft: WorkspaceSetupProviderDraft = {
      id: preset.defaultId ? uniqueProviderId(preset.defaultId, providers) : '',
      displayName: preset.defaultDisplayName,
      protocol: connection.protocol,
      backendId: connection.backendId,
      apiKey: '',
      endPoint: connection.endPoint,
      networkTimeoutSeconds: null,
      authMethod: connection.authMethod
    }
    return [preset.id, { draft, timeout: '', idEdited: false, advanced: false }]
  }))
}

export function setupAuthMode(preset: ProviderPreset, connection: SetupConnection): ProviderPreset['auth'][number]['mode'] {
  return providerPresetAuth(preset, connection.draft.authMethod ?? '').mode
}

export function finalDraft(
  preset: ProviderPreset,
  connection: SetupConnection,
  providers: WorkspaceSetupProviderSummary[]
): WorkspaceSetupProviderDraft {
  const { draft } = connection
  const id = preset.id === 'custom' && !connection.idEdited ? derivedProviderId(connection, providers) : draft.id.trim()
  return {
    ...draft,
    id,
    displayName: draft.displayName.trim() || id,
    apiKey: draft.apiKey.trim(),
    endPoint: draft.endPoint.trim(),
    networkTimeoutSeconds: connection.timeout.trim() ? Number(connection.timeout.trim()) : null
  }
}

export function connectionValid(preset: ProviderPreset, connection: SetupConnection, signIn: SetupSignInState): boolean {
  const { draft } = connection
  if (!timeoutValid(connection.timeout)) return false
  if (connection.idEdited && !draft.id.trim()) return false
  if (setupAuthMode(preset, connection) === 'signIn') return signIn.phase === 'signedIn'
  if (!isHttpUrl(draft.endPoint)) return false
  if (preset.id === 'custom') return draft.displayName.trim().length > 0
  return draft.apiKey.trim().length > 0
}

export function SetupChoiceRow({
  selected,
  mark,
  title,
  hint,
  aside,
  onSelect,
  children
}: {
  selected: boolean
  mark: ReactNode
  title: string
  hint: string
  aside?: ReactNode
  onSelect(): void
  children?: ReactNode
}): JSX.Element {
  const rowRef = useRef<HTMLDivElement | null>(null)
  const hasBody = children != null
  useEffect(() => {
    if (selected && hasBody) rowRef.current?.scrollIntoView?.({ block: 'nearest', behavior: 'smooth' })
  }, [hasBody, selected])
  return (
    <div className="workspace-setup__choice" data-selected={selected || undefined} ref={rowRef}>
      <button type="button" role="radio" aria-checked={selected} className="workspace-setup__choice-head" onClick={onSelect}>
        {mark}
        <span className="workspace-setup__row-text">
          <span className="workspace-setup__row-title">{title}</span>
          <span className="workspace-setup__row-hint">{hint}</span>
        </span>
        {aside}
        <span className="workspace-setup__radio" aria-hidden="true" />
      </button>
      {selected && children ? <div className="workspace-setup__choice-body">{children}</div> : null}
    </div>
  )
}

function PresetMark({ preset }: { preset: ProviderPreset | null }): JSX.Element {
  return (
    <span className="workspace-setup__mark">
      <ProviderPresetMark mark={preset?.mark ?? null} />
    </span>
  )
}

export function SetupAccessSection({
  presets,
  providers,
  defaultProviderId,
  access,
  showMore,
  connections,
  signIn,
  onShowMore,
  onSelect,
  onUpdate
}: {
  presets: readonly ProviderPreset[]
  providers: WorkspaceSetupProviderSummary[]
  defaultProviderId: string | null
  access: SetupAccessChoice
  showMore: boolean
  connections: SetupConnections
  signIn: SetupSignInState
  onShowMore(expanded: boolean): void
  onSelect(choice: SetupAccessChoice): void
  onUpdate(preset: ProviderPresetId, partial: SetupConnectionPatch): void
}): JSX.Element {
  const t = useT()
  const hasSaved = providers.length > 0
  const choices = (
    <div className="workspace-setup__group" role="radiogroup" aria-label={t('setupWizard.access.add')}>
      {presets.map((preset) => {
        const connection = connections[preset.id]
        return (
          <SetupChoiceRow
            key={preset.id}
            selected={access === preset.id}
            mark={<PresetMark preset={preset} />}
            title={preset.label}
            hint={preset.hint}
            onSelect={() => onSelect(preset.id)}
          >
            {connection && (
              <PresetConnectionBody
                preset={preset}
                connection={connection}
                providers={providers}
                signIn={signIn}
                onUpdate={(partial) => onUpdate(preset.id, partial)}
              />
            )}
          </SetupChoiceRow>
        )
      })}
    </div>
  )
  return (
    <div className="workspace-setup__stack">
      {hasSaved && (
        <section>
          <h2 className="workspace-setup__group-label">{t('setupWizard.access.saved')}</h2>
          <div className="workspace-setup__group" role="radiogroup" aria-label={t('setupWizard.access.saved')}>
            {providers.map((provider) => {
              const preset = findProviderPreset(presets, providerPresetIdFor(provider))
              const auth = preset ? providerPresetAuth(preset, provider.authMethod ?? '') : null
              return (
                <SetupChoiceRow
                  key={provider.id}
                  selected={access === `saved:${provider.id}`}
                  mark={<PresetMark preset={preset} />}
                  title={provider.displayName}
                  hint={[preset?.label ?? provider.backendId, auth?.label].filter(Boolean).join(' · ')}
                  aside={provider.id === defaultProviderId
                    ? <span className="workspace-setup__tag">{t('setupWizard.access.savedDefault')}</span>
                    : undefined}
                  onSelect={() => onSelect(`saved:${provider.id}`)}
                />
              )
            })}
          </div>
        </section>
      )}
      {hasSaved
        ? (
            <section className="workspace-setup__disclosure">
              <DisclosureButton expanded={showMore} onClick={() => onShowMore(!showMore)}>
                {t('setupWizard.access.add')}
              </DisclosureButton>
              {showMore && choices}
            </section>
          )
        : choices}
    </div>
  )
}

function PresetConnectionBody({
  preset,
  connection,
  providers,
  signIn,
  onUpdate
}: {
  preset: ProviderPreset
  connection: SetupConnection
  providers: WorkspaceSetupProviderSummary[]
  signIn: SetupSignInState
  onUpdate(partial: SetupConnectionPatch): void
}): JSX.Element {
  const t = useT()
  const auth = providerPresetAuth(preset, connection.draft.authMethod ?? '')
  return (
    <div className="workspace-setup__fields">
      {preset.auth.length > 1 && (
        <div>
          <SegmentedControl
            value={auth.method}
            ariaLabel={t('providerPreset.authMethod')}
            options={preset.auth.map((item) => ({ value: item.method, label: item.label }))}
            onChange={(authMethod) => onUpdate({ draft: { authMethod, apiKey: '' } })}
          />
        </div>
      )}
      {auth.mode === 'signIn'
        ? <SignInStatus signIn={signIn} />
        : <ConnectionFields preset={preset} connection={connection} providers={providers} onUpdate={onUpdate} />}
    </div>
  )
}

function SignInStatus({ signIn }: { signIn: SetupSignInState }): JSX.Element {
  const t = useT()
  if (signIn.phase === 'signedIn') {
    return (
      <div className="workspace-setup__status" data-level="success">
        <Check size={14} strokeWidth={2.4} aria-hidden="true" />
        {t('setupWizard.access.signedIn')}
      </div>
    )
  }
  if (signIn.phase === 'failed') {
    return (
      <div className="workspace-setup__status" data-level="error" role="alert">
        {t('setupWizard.access.signInFailed', { error: signIn.message })}
      </div>
    )
  }
  return (
    <div className="workspace-setup__status">
      {signIn.phase === 'pending' ? t('setupWizard.access.signInWaiting') : t('setupWizard.access.signInBody')}
    </div>
  )
}

function ConnectionFields({
  preset,
  connection,
  providers,
  onUpdate
}: {
  preset: ProviderPreset
  connection: SetupConnection
  providers: WorkspaceSetupProviderSummary[]
  onUpdate(partial: SetupConnectionPatch): void
}): JSX.Element {
  const t = useT()
  const { draft } = connection
  const custom = preset.id === 'custom'
  const endpointInvalid = draft.endPoint.trim().length > 0 && !isHttpUrl(draft.endPoint)
  const shownId = custom && !connection.idEdited ? derivedProviderId(connection, providers) : draft.id
  const endpointField = (
    <Field label={t('setupWizard.field.endpoint')} error={endpointInvalid ? t('setupWizard.validation.endpoint') : null}>
      <Input
        value={draft.endPoint}
        invalid={endpointInvalid}
        placeholder={custom ? 'https://' : preset.endPoint}
        onChange={(event) => onUpdate({ draft: { endPoint: event.target.value } })}
        mono
      />
    </Field>
  )
  return (
    <>
      {custom && (
        <>
          <div className="workspace-setup__field-pair">
            <Field label={t('setupWizard.field.name')}>
              <Input
                value={draft.displayName}
                placeholder={t('setupWizard.field.namePlaceholder')}
                onChange={(event) => onUpdate({ draft: { displayName: event.target.value } })}
              />
            </Field>
            <Field label={t('providerPreset.apiFormat')}>
              <SettingsSelect<DesktopProviderProtocol>
                value={draft.protocol as DesktopProviderProtocol}
                ariaLabel={t('providerPreset.apiFormat')}
                style={{ width: '100%' }}
                onValueChange={(protocol) => onUpdate({ draft: { protocol } })}
                options={apiFormatOptions(t)}
              />
            </Field>
          </div>
          {endpointField}
        </>
      )}
      <Field label={custom ? t('setupWizard.field.apiKeyOptional') : t('setupWizard.field.apiKey')}>
        <SecretInput
          value={draft.apiKey}
          ariaLabel={t('setupWizard.field.apiKey')}
          placeholder={preset.id === 'anthropic' ? 'sk-ant-…' : 'sk-…'}
          onChange={(apiKey) => onUpdate({ draft: { apiKey } })}
          mono
        />
      </Field>
      <DisclosureButton expanded={connection.advanced} onClick={() => onUpdate({ advanced: !connection.advanced })}>
        {t('setupWizard.field.advanced')}
      </DisclosureButton>
      {connection.advanced && (
        <>
          {!custom && endpointField}
          <div className="workspace-setup__field-pair">
            <Field label={t('setupWizard.field.timeout')}>
              <Input
                type="number"
                className="dc-plain-number"
                min={1}
                value={connection.timeout}
                invalid={!timeoutValid(connection.timeout)}
                placeholder="600"
                onChange={(event) => onUpdate({ timeout: event.target.value })}
              />
            </Field>
            <Field label={t('setupWizard.field.id')}>
              <Input
                value={shownId}
                mono
                invalid={connection.idEdited && !draft.id.trim()}
                onChange={(event) => onUpdate({ idEdited: true, draft: { id: slugProviderId(event.target.value) } })}
              />
            </Field>
          </div>
        </>
      )}
    </>
  )
}

function Field({ label, error, children }: { label: string; error?: string | null; children: ReactNode }): JSX.Element {
  return (
    <label className="workspace-setup__field">
      <span className="workspace-setup__field-label">{label}</span>
      {children}
      {error ? <span className="workspace-setup__field-error">{error}</span> : null}
    </label>
  )
}
