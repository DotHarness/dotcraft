import type { CSSProperties, JSX, ReactNode } from 'react'

import { useLocale, useT } from '../../contexts/LocaleContext'
import type { MessageKey } from '../../../shared/locales'
import { resolveSettingsDocsUrl, type SettingsDocsTopic } from './settingsDocs'

interface SettingsLearnMoreLinkProps {
  topic: SettingsDocsTopic
  aboutKey?: MessageKey | string
}

interface SettingsDescriptionWithLearnMoreProps extends SettingsLearnMoreLinkProps {
  children: ReactNode
}

export function SettingsLearnMoreLink({ topic, aboutKey }: SettingsLearnMoreLinkProps): JSX.Element {
  const locale = useLocale()
  const t = useT()
  const topicLabel = aboutKey ? t(aboutKey) : null
  const ariaLabel = topicLabel
    ? t('settings.learnMoreAbout', { topic: topicLabel })
    : t('settings.learnMore')

  return (
    <button
      type="button"
      aria-label={ariaLabel}
      onClick={() => void window.api.shell.openExternal(resolveSettingsDocsUrl(topic, locale))}
      style={learnMoreButtonStyle}
    >
      {t('settings.learnMore')}
    </button>
  )
}

export function SettingsDescriptionWithLearnMore({
  children,
  topic,
  aboutKey
}: SettingsDescriptionWithLearnMoreProps): JSX.Element {
  return (
    <>
      {children}{' '}
      <SettingsLearnMoreLink topic={topic} aboutKey={aboutKey} />
    </>
  )
}

const learnMoreButtonStyle: CSSProperties = {
  border: 'none',
  background: 'transparent',
  color: 'var(--accent)',
  padding: 0,
  font: 'inherit',
  cursor: 'pointer'
}
