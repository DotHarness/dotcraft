import { useEffect, useRef, type ReactNode } from 'react'
import { Check, Plug } from 'lucide-react'
import {
  ANTHROPIC_PROTOCOL,
  defaultProviderEndpoint,
  DESKTOP_PROVIDER_PROTOCOLS,
  OPENAI_RESPONSES_PROTOCOL
} from '../../../shared/providerProtocols'
import type {
  WorkspaceSetupProviderDraft,
  WorkspaceSetupProviderProtocol,
  WorkspaceSetupProviderSummary
} from '../../../preload/api.d'
import { useT } from '../../contexts/LocaleContext'
import { slugProviderId, uniqueProviderId } from '../../utils/providerId'
import { SecretInput } from '../channels/FormShared'
import { SettingsSelect } from '../settings/ui/SettingsSelect'
import { DisclosureChevron } from '../ui/DisclosureChevron'
import { Input } from '../ui/Input'
import { ProviderMark } from '../ui/ProviderMark'

export type SetupConnectionKind = 'chatgpt' | 'openai' | 'anthropic' | 'other'
export type SetupAccessChoice = SetupConnectionKind | `saved:${string}`
export type SetupSignInState = { phase: 'idle' } | { phase: 'pending' } | { phase: 'signedIn' } | { phase: 'failed'; message: string }
export interface SetupConnection { draft: WorkspaceSetupProviderDraft; timeout: string; idEdited: boolean; advanced: boolean }
export type SetupConnectionPatch = Partial<Omit<SetupConnection, 'draft'>> & { draft?: Partial<WorkspaceSetupProviderDraft> }

const CONNECTION_KINDS: SetupConnectionKind[] = ['chatgpt', 'openai', 'anthropic', 'other']

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

export function initialConnections(providers: WorkspaceSetupProviderSummary[]): Record<SetupConnectionKind, SetupConnection> {
  const make = (draft: WorkspaceSetupProviderDraft): SetupConnection => ({ draft, timeout: '', idEdited: false, advanced: false })
  return {
    chatgpt: make({
      id: uniqueProviderId('chatgpt', providers),
      displayName: 'ChatGPT',
      protocol: OPENAI_RESPONSES_PROTOCOL,
      apiKey: '',
      endPoint: defaultProviderEndpoint(OPENAI_RESPONSES_PROTOCOL),
      networkTimeoutSeconds: null,
      authMethod: 'chatgptOAuth'
    }),
    openai: make({
      id: uniqueProviderId('openai', providers),
      displayName: 'OpenAI',
      protocol: OPENAI_RESPONSES_PROTOCOL,
      apiKey: '',
      endPoint: defaultProviderEndpoint(OPENAI_RESPONSES_PROTOCOL),
      networkTimeoutSeconds: null,
      authMethod: 'apiKey'
    }),
    anthropic: make({
      id: uniqueProviderId('anthropic', providers),
      displayName: 'Anthropic',
      protocol: ANTHROPIC_PROTOCOL,
      apiKey: '',
      endPoint: defaultProviderEndpoint(ANTHROPIC_PROTOCOL),
      networkTimeoutSeconds: null,
      authMethod: 'apiKey'
    }),
    other: make({
      id: '',
      displayName: '',
      protocol: OPENAI_RESPONSES_PROTOCOL,
      apiKey: '',
      endPoint: '',
      networkTimeoutSeconds: null,
      authMethod: 'apiKey'
    })
  }
}

export function finalDraft(
  kind: SetupConnectionKind,
  connection: SetupConnection,
  providers: WorkspaceSetupProviderSummary[]
): WorkspaceSetupProviderDraft {
  const { draft } = connection
  const id = kind === 'other' && !connection.idEdited ? derivedProviderId(connection, providers) : draft.id.trim()
  return {
    ...draft,
    id,
    displayName: draft.displayName.trim() || id,
    apiKey: draft.apiKey.trim(),
    endPoint: draft.endPoint.trim(),
    networkTimeoutSeconds: connection.timeout.trim() ? Number(connection.timeout.trim()) : null
  }
}

export function connectionValid(kind: SetupConnectionKind, connection: SetupConnection, signIn: SetupSignInState): boolean {
  const { draft } = connection
  if (!timeoutValid(connection.timeout)) return false
  if (connection.idEdited && !draft.id.trim()) return false
  if (kind === 'chatgpt') return signIn.phase === 'signedIn'
  if (!isHttpUrl(draft.endPoint)) return false
  if (kind === 'other') return draft.displayName.trim().length > 0
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

function KindMark({ kind }: { kind: 'openai' | 'anthropic' | 'other' }): JSX.Element {
  return (
    <span className="workspace-setup__mark">
      {kind === 'other' ? <Plug size={16} strokeWidth={1.8} /> : <ProviderMark kind={kind} size={16} />}
    </span>
  )
}

export function SetupAccessSection({
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
  providers: WorkspaceSetupProviderSummary[]
  defaultProviderId: string | null
  access: SetupAccessChoice
  showMore: boolean
  connections: Record<SetupConnectionKind, SetupConnection>
  signIn: SetupSignInState
  onShowMore(): void
  onSelect(choice: SetupAccessChoice): void
  onUpdate(kind: SetupConnectionKind, partial: SetupConnectionPatch): void
}): JSX.Element {
  const t = useT()
  return (
    <div className="workspace-setup__stack">
      {providers.length > 0 && (
        <section>
          <h2 className="workspace-setup__group-label">{t('setupWizard.access.saved')}</h2>
          <div className="workspace-setup__group" role="radiogroup" aria-label={t('setupWizard.access.saved')}>
            {providers.map((provider) => (
              <SetupChoiceRow
                key={provider.id}
                selected={access === `saved:${provider.id}`}
                mark={<KindMark kind={provider.protocol === ANTHROPIC_PROTOCOL ? 'anthropic' : 'openai'} />}
                title={provider.displayName}
                hint={provider.authMethod === 'chatgptOAuth' ? t('setupWizard.access.savedSignIn') : t('setupWizard.access.savedKey')}
                aside={provider.id === defaultProviderId
                  ? <span className="workspace-setup__tag">{t('setupWizard.access.savedDefault')}</span>
                  : undefined}
                onSelect={() => onSelect(`saved:${provider.id}`)}
              />
            ))}
          </div>
        </section>
      )}
      {showMore ? (
        <section>
          {providers.length > 0 && <h2 className="workspace-setup__group-label">{t('setupWizard.access.add')}</h2>}
          <div className="workspace-setup__group" role="radiogroup" aria-label={t('setupWizard.access.add')}>
            {CONNECTION_KINDS.map((kind) => (
              <SetupChoiceRow
                key={kind}
                selected={access === kind}
                mark={<KindMark kind={kind === 'chatgpt' ? 'openai' : kind} />}
                title={t(`setupWizard.access.${kind}`)}
                hint={t(`setupWizard.access.${kind}Hint`)}
                onSelect={() => onSelect(kind)}
              >
                {kind === 'chatgpt'
                  ? <ChatGptSignInStatus signIn={signIn} />
                  : (
                      <ConnectionFields
                        kind={kind}
                        connection={connections[kind]}
                        providers={providers}
                        onUpdate={(partial) => onUpdate(kind, partial)}
                      />
                    )}
              </SetupChoiceRow>
            ))}
          </div>
        </section>
      ) : (
        <button type="button" className="workspace-setup__more" onClick={onShowMore}>
          <DisclosureChevron expanded={false} />
          {t('setupWizard.access.addMore')}
        </button>
      )}
    </div>
  )
}

function ChatGptSignInStatus({ signIn }: { signIn: SetupSignInState }): JSX.Element {
  const t = useT()
  if (signIn.phase === 'signedIn') {
    return (
      <div className="workspace-setup__status" data-level="success">
        <Check size={14} strokeWidth={2.4} aria-hidden="true" />
        {t('setupWizard.access.chatgptSignedIn')}
      </div>
    )
  }
  if (signIn.phase === 'failed') {
    return (
      <div className="workspace-setup__status" data-level="error" role="alert">
        {t('setupWizard.access.chatgptFailed', { error: signIn.message })}
      </div>
    )
  }
  return (
    <div className="workspace-setup__status">
      {signIn.phase === 'pending' ? t('setupWizard.access.chatgptWaiting') : t('setupWizard.access.chatgptBody')}
    </div>
  )
}

function ConnectionFields({
  kind,
  connection,
  providers,
  onUpdate
}: {
  kind: Exclude<SetupConnectionKind, 'chatgpt'>
  connection: SetupConnection
  providers: WorkspaceSetupProviderSummary[]
  onUpdate(partial: SetupConnectionPatch): void
}): JSX.Element {
  const t = useT()
  const { draft } = connection
  const endpointInvalid = draft.endPoint.trim().length > 0 && !isHttpUrl(draft.endPoint)
  const shownId = kind === 'other' && !connection.idEdited ? derivedProviderId(connection, providers) : draft.id
  const endpointField = (
    <Field label={t('setupWizard.field.endpoint')} error={endpointInvalid ? t('setupWizard.validation.endpoint') : null}>
      <Input
        value={draft.endPoint}
        invalid={endpointInvalid}
        placeholder={kind === 'other' ? 'https://' : defaultProviderEndpoint(draft.protocol)}
        onChange={(event) => onUpdate({ draft: { endPoint: event.target.value } })}
        mono
      />
    </Field>
  )
  return (
    <div className="workspace-setup__fields">
      {kind === 'other' && (
        <>
          <div className="workspace-setup__field-pair">
            <Field label={t('setupWizard.field.name')}>
              <Input
                value={draft.displayName}
                placeholder={t('setupWizard.field.namePlaceholder')}
                onChange={(event) => onUpdate({ draft: { displayName: event.target.value } })}
              />
            </Field>
            <Field label={t('setupWizard.field.format')}>
              <SettingsSelect<WorkspaceSetupProviderProtocol>
                value={draft.protocol}
                ariaLabel={t('setupWizard.field.format')}
                style={{ width: '100%' }}
                onValueChange={(protocol) => onUpdate({ draft: { protocol } })}
                options={DESKTOP_PROVIDER_PROTOCOLS.map((protocol) => ({ value: protocol, label: t(`setupWizard.format.${protocol}`) }))}
              />
            </Field>
          </div>
          {endpointField}
        </>
      )}
      <Field label={kind === 'other' ? t('setupWizard.field.apiKeyOptional') : t('setupWizard.field.apiKey')}>
        <SecretInput
          value={draft.apiKey}
          ariaLabel={t('setupWizard.field.apiKey')}
          placeholder={kind === 'anthropic' ? 'sk-ant-…' : 'sk-…'}
          onChange={(apiKey) => onUpdate({ draft: { apiKey } })}
          mono
        />
      </Field>
      <button
        type="button"
        className="workspace-setup__more"
        aria-expanded={connection.advanced}
        onClick={() => onUpdate({ advanced: !connection.advanced })}
      >
        <DisclosureChevron expanded={connection.advanced} />
        {t('setupWizard.field.advanced')}
      </button>
      {connection.advanced && (
        <>
          {kind !== 'other' && endpointField}
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
    </div>
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
