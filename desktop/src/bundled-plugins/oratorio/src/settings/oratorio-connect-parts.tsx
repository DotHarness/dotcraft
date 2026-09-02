import { Children, cloneElement, isValidElement, useId, type ReactNode } from 'react'
import { GithubGlyph, GitlabGlyph } from '../ProviderGlyphs'
import { oratorioHost } from '../runtime'
import { useOratorioConnectT } from './oratorio-connect-i18n'
import { connectDocsUrl } from './oratorio-connect-model'
import type { SourceProvider } from './oratorio-settings-model'

export function providerName(provider: SourceProvider): string {
  return provider === 'github' ? 'GitHub' : 'GitLab'
}

export function ProviderGlyph({ provider, size = 15 }: { provider: SourceProvider; size?: number }): JSX.Element {
  return provider === 'github' ? <GithubGlyph size={size} /> : <GitlabGlyph size={size} />
}

export function StepHeading({ title, description }: { title: string; description: ReactNode }): JSX.Element {
  return <div className="ora-connect__heading"><h2>{title}</h2><p>{description}</p></div>
}

/** A single control child receives the generated id so the visible label names it. */
export function ConnectField({ label, hint, error, children }: { label: string; hint?: ReactNode; error?: string; children: ReactNode }): JSX.Element {
  const id = `ora-connect-${useId()}`
  const items = Children.toArray(children)
  const control = items.length === 1 && isValidElement<{ id?: string }>(items[0]) && typeof items[0].type !== 'string' ? cloneElement(items[0], { id }) : children
  return (
    <div className="ora-connect__field">
      <label htmlFor={items.length === 1 && control !== children ? id : undefined}>{label}</label>
      {control}
      {error ? <small className="ora-connect__error" role="alert">{error}</small> : hint ? <small className="ora-connect__hint">{hint}</small> : null}
    </div>
  )
}

export function LearnMore({ provider }: { provider: SourceProvider }): JSX.Element {
  const t = useOratorioConnectT()
  const host = oratorioHost()
  return (
    <button type="button" className="ora-connect__learn-more" onClick={() => void host.navigation.openExternal(connectDocsUrl(provider, host.environment.locale))}>
      {t('learnMore')}
    </button>
  )
}

/** The plugin UI kit has no SelectionCard, so the wizard carries its own radio card. */
export function ChoiceCard({ name, value, active, disabled, title, description, icon, onSelect }: {
  name: string
  value: string
  active: boolean
  disabled?: boolean
  title: string
  description?: string
  icon?: ReactNode
  onSelect: () => void
}): JSX.Element {
  const id = useId()
  return (
    <label className="ora-connect__choice" data-active={active ? 'true' : undefined} htmlFor={id}>
      <input id={id} type="radio" name={name} value={value} checked={active} disabled={disabled} onChange={onSelect} />
      {icon ? <span className="ora-connect__choice-mark" aria-hidden="true">{icon}</span> : <span className="ora-connect__choice-radio" aria-hidden="true" />}
      <span className="ora-connect__choice-body"><strong>{title}</strong>{description ? <span>{description}</span> : null}</span>
    </label>
  )
}

export function QuietRow({ children, action }: { children: ReactNode; action?: ReactNode }): JSX.Element {
  return <div className="ora-connect__quiet-row"><div>{children}</div>{action ? <div className="ora-connect__quiet-row-action">{action}</div> : null}</div>
}
