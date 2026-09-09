import { useEffect, useState, type CSSProperties, type JSX } from 'react'
import { Bot } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { MenuHeading, MenuOption, PillDropdown } from '../ui/PillDropdown'
import { RobotAvatar } from '../agents/RobotAvatar'

interface ProfileEntry {
  id: string
  name?: string
  description?: string
  source: string
  valid?: boolean
  shadowed?: boolean
}

interface AgentProfileDropdownProps {
  /** Selected Agent Profile id, or null/undefined for the default automation agent. */
  value: string | null | undefined
  /** Emits the chosen profile id, or null when the default agent is selected. */
  onChange(profileId: string | null): void
}

/**
 * The profile governs the run's capabilities; automation still forces its operational
 * fields. The first row clears the selection back to the default automation agent.
 */
export function AgentProfileDropdown({ value, onChange }: AgentProfileDropdownProps): JSX.Element {
  const t = useT()
  const [profiles, setProfiles] = useState<ProfileEntry[]>([])
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    void window.api.appServer
      .sendRequest('agent/profiles/list', { includeInvalid: false })
      .then((res) => {
        if (cancelled) return
        const list = ((res as { profiles?: ProfileEntry[] }).profiles ?? []).filter(
          (p) => !p.shadowed && p.valid !== false
        )
        setProfiles(list)
      })
      .catch(() => {
        if (!cancelled) setProfiles([])
      })
      .finally(() => {
        if (cancelled) return
        setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  const selected = value ? profiles.find((p) => p.id === value) : undefined

  const label = value ? (selected?.name || selected?.id || value) : t('auto.newTask.agentDefault')
  const icon =
    value && selected ? (
      <RobotAvatar name={selected.name || selected.id} size={16} />
    ) : (
      <Bot size={13} strokeWidth={1.8} aria-hidden />
    )

  return (
    <PillDropdown
      ariaLabel={t('auto.newTask.agentLabel')}
      label={label}
      icon={icon}
      accent={!!value}
      panelMinWidth={260}
    >
      {(close) => (
        <>
          <MenuHeading>{t('auto.newTask.agentLabel')}</MenuHeading>
          <MenuOption
            selected={!value}
            icon={<Bot size={16} strokeWidth={1.8} aria-hidden />}
            description={t('auto.newTask.agentDefaultHint')}
            onClick={() => {
              onChange(null)
              close()
            }}
          >
            {t('auto.newTask.agentDefault')}
          </MenuOption>
          {loading && profiles.length === 0 && (
            <div style={STATE_STYLE}>{t('composer.profile.loading')}</div>
          )}
          {profiles.map((profile) => (
            <MenuOption
              key={`${profile.source}:${profile.id}`}
              selected={profile.id === value}
              icon={<RobotAvatar name={profile.name || profile.id} size={20} />}
              description={profile.description}
              onClick={() => {
                onChange(profile.id)
                close()
              }}
            >
              {profile.name || profile.id}
            </MenuOption>
          ))}
        </>
      )}
    </PillDropdown>
  )
}

const STATE_STYLE: CSSProperties = {
  padding: '8px 9px',
  fontSize: '12px',
  color: 'var(--text-dimmed)'
}
