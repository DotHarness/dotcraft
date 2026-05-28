import type { CSSProperties, JSX, KeyboardEvent, MouseEvent } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import type { MessageKey } from '../../../../../shared/locales'
import { SettingsGroup } from '../../SettingsGroup'
import { PillSwitch } from '../../../ui/PillSwitch'
import { AgentIcon } from './AgentIcon'
import { PRESET_PROFILE_NAMES, type SubAgentProfileEntryWire } from './wire'
import { pillBadgeStyle, primaryButtonStyle } from './styles'

interface SubAgentListProps {
  profiles: SubAgentProfileEntryWire[]
  loading: boolean
  togglingName: string | null
  onOpenProfile: (profile: SubAgentProfileEntryWire) => void
  onToggleEnabled: (profile: SubAgentProfileEntryWire, nextEnabled: boolean) => void
  onAddCustom: () => void
}

export function SubAgentList({
  profiles,
  loading,
  togglingName,
  onOpenProfile,
  onToggleEnabled,
  onAddCustom
}: SubAgentListProps): JSX.Element {
  const t = useT()

  const presetProfiles = orderPresets(profiles)
  const customProfiles = profiles.filter((profile) => !profile.isBuiltIn && !profile.isTemplate)

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
      <SettingsGroup
        title={t('settings.subAgents.list.presetSection')}
        flush
      >
        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
          {loading && presetProfiles.length === 0 && (
            <div style={emptyNoticeStyle()}>{t('settings.subAgents.loading')}</div>
          )}
          {presetProfiles.map((profile) => (
            <ProfileCard
              key={profile.name}
              profile={profile}
              togglingName={togglingName}
              onOpen={onOpenProfile}
              onToggleEnabled={onToggleEnabled}
            />
          ))}
        </div>
      </SettingsGroup>

      <SettingsGroup
        title={t('settings.subAgents.list.customSection')}
        flush
      >
        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
          {customProfiles.length === 0 && (
            <div style={emptyNoticeStyle()}>{t('settings.subAgents.list.customEmpty')}</div>
          )}
          {customProfiles.map((profile) => (
            <ProfileCard
              key={profile.name}
              profile={profile}
              togglingName={togglingName}
              onOpen={onOpenProfile}
              onToggleEnabled={onToggleEnabled}
            />
          ))}
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: '12px',
              padding: '12px 14px',
              borderRadius: '10px',
              border: '1px dashed var(--border-default)'
            }}
          >
            <div style={{ minWidth: 0 }}>
              <div style={{ fontSize: '13px', fontWeight: 600, color: 'var(--text-primary)' }}>
                {t('settings.subAgents.list.addCustomAgent')}
              </div>
              <div style={{ fontSize: '12px', color: 'var(--text-dimmed)', marginTop: '2px' }}>
                {t('settings.subAgents.list.addCustomAgentHint')}
              </div>
            </div>
            <button
              type="button"
              onClick={onAddCustom}
              style={primaryButtonStyle(false)}
            >
              {t('settings.subAgents.list.addCustomAgent')}
            </button>
          </div>
        </div>
      </SettingsGroup>
    </div>
  )
}

interface ProfileCardProps {
  profile: SubAgentProfileEntryWire
  togglingName: string | null
  onOpen: (profile: SubAgentProfileEntryWire) => void
  onToggleEnabled: (profile: SubAgentProfileEntryWire, nextEnabled: boolean) => void
}

function ProfileCard({ profile, togglingName, onOpen, onToggleEnabled }: ProfileCardProps): JSX.Element {
  const t = useT()
  const toggleDisabled = profile.isDefault || togglingName === profile.name
  const subtitle = resolveSubtitle(profile, t)
  const handleKey = (event: KeyboardEvent<HTMLDivElement>): void => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault()
      onOpen(profile)
    }
  }

  const stopClick = (event: MouseEvent<HTMLDivElement>): void => {
    event.stopPropagation()
  }
  const stopKey = (event: KeyboardEvent<HTMLDivElement>): void => {
    event.stopPropagation()
  }

  return (
    <div
      role="button"
      tabIndex={0}
      aria-label={`Open sub-agent profile ${profile.name}`}
      onClick={() => onOpen(profile)}
      onKeyDown={handleKey}
      style={cardStyle()}
    >
      <AgentIcon name={profile.name} isBuiltIn={profile.isBuiltIn} size={32} />
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
          <span style={titleStyle()}>{resolveTitle(profile, t)}</span>
          {profile.isDefault && (
            <span style={pillBadgeStyle('neutral')}>{t('settings.subAgents.card.defaultBadge')}</span>
          )}
          {profile.hasWorkspaceOverride && profile.isBuiltIn && (
            <span style={pillBadgeStyle('accent')}>
              {t('settings.subAgents.card.customizedBadge')}
            </span>
          )}
          {!profile.isBuiltIn && (
            <span style={pillBadgeStyle('accent')}>{t('settings.subAgents.card.customBadge')}</span>
          )}
        </div>
        <div style={subtitleStyle()}>{subtitle}</div>
        {!profile.diagnostic.binaryResolved && profile.definition.runtime !== 'native' && (
          <div style={{ ...subtitleStyle(), color: 'var(--warning, #ff9500)' }}>
            {t('settings.subAgents.card.binaryMissing')}
          </div>
        )}
      </div>
      <div
        style={{ display: 'flex', alignItems: 'center' }}
        onClick={stopClick}
        onKeyDown={stopKey}
      >
        <PillSwitch
          aria-label={t('settings.subAgents.toggleAria', { name: profile.name })}
          checked={profile.enabled}
          onChange={(next) => onToggleEnabled(profile, next)}
          disabled={toggleDisabled}
        />
      </div>
    </div>
  )
}

function orderPresets(profiles: SubAgentProfileEntryWire[]): SubAgentProfileEntryWire[] {
  const presets: SubAgentProfileEntryWire[] = []
  for (const name of PRESET_PROFILE_NAMES) {
    const entry = profiles.find((profile) => profile.name === name)
    if (entry) presets.push(entry)
  }
  return presets
}

function resolveTitle(
  profile: SubAgentProfileEntryWire,
  t: (key: MessageKey | string, vars?: Record<string, string | number>) => string
): string {
  if (profile.name === 'native') return t('settings.subAgents.preset.native.title')
  if (profile.name === 'codex-cli') return t('settings.subAgents.preset.codex.title')
  if (profile.name === 'cursor-cli') return t('settings.subAgents.preset.cursor.title')
  return profile.name
}

function resolveSubtitle(
  profile: SubAgentProfileEntryWire,
  t: (key: MessageKey | string, vars?: Record<string, string | number>) => string
): string {
  if (profile.name === 'native') return t('settings.subAgents.card.nativeSubtitle')
  if (profile.name === 'codex-cli') return t('settings.subAgents.card.codexSubtitle')
  if (profile.name === 'cursor-cli') return t('settings.subAgents.card.cursorSubtitle')
  const binary = profile.definition.bin?.trim()
  if (binary) return t('settings.subAgents.card.customSubtitle', { binary })
  return t('settings.subAgents.card.customSubtitleFallback')
}

function cardStyle(): CSSProperties {
  return {
    display: 'flex',
    alignItems: 'center',
    gap: '14px',
    padding: '12px 14px',
    borderRadius: '10px',
    border: '1px solid var(--border-default)',
    background: 'var(--bg-primary)',
    cursor: 'pointer',
    textAlign: 'left'
  }
}

function titleStyle(): CSSProperties {
  return {
    fontSize: '14px',
    fontWeight: 600,
    color: 'var(--text-primary)',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap'
  }
}

function subtitleStyle(): CSSProperties {
  return {
    fontSize: '12px',
    color: 'var(--text-dimmed)',
    marginTop: '2px',
    lineHeight: 1.5
  }
}

function emptyNoticeStyle(): CSSProperties {
  return {
    padding: '12px 14px',
    borderRadius: '10px',
    border: '1px dashed var(--border-default)',
    color: 'var(--text-dimmed)',
    fontSize: '12px'
  }
}
