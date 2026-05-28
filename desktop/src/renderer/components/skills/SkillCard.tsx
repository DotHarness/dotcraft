import { useT } from '../../contexts/LocaleContext'
import type { SkillEntry } from '../../stores/skillsStore'
import { SkillAvatar } from './SkillAvatar'
import { PillSwitch } from '../ui/PillSwitch'

interface SkillCardProps {
  skill: SkillEntry
  onOpen: () => void
  onToggleEnabled: (enabled: boolean) => void
}

/**
 * Single skill row in the grid: generic glyph, title, description, source badge, enable switch.
 */
export function SkillCard({ skill, onOpen, onToggleEnabled }: SkillCardProps): JSX.Element {
  const t = useT()

  function handleSwitchClick(e: React.MouseEvent): void {
    e.stopPropagation()
  }

  return (
    <div
      role="button"
      tabIndex={0}
      onClick={onOpen}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault()
          onOpen()
        }
      }}
      style={{
        display: 'flex',
        alignItems: 'flex-start',
        gap: '12px',
        padding: '12px 14px',
        borderRadius: '8px',
        border: '1px solid var(--border-default)',
        backgroundColor: 'var(--bg-secondary)',
        cursor: 'pointer',
        opacity: skill.enabled ? 1 : 0.65,
        transition: 'background-color 120ms ease'
      }}
      onMouseEnter={(e) => {
        ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'var(--bg-tertiary)'
      }}
      onMouseLeave={(e) => {
        ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'var(--bg-secondary)'
      }}
    >
      <SkillAvatar
        name={skill.name}
        displayName={skill.displayName || skill.name}
        size={40}
        iconDataUrl={skill.iconSmallDataUrl}
      />
      <div style={{ flex: 1, minWidth: 0 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap' }}>
          <span style={{ fontWeight: 600, fontSize: '14px', color: 'var(--text-primary)' }}>
            {skill.displayName || skill.name}
          </span>
          <SourceBadge source={skill.source} t={t} />
          {!skill.available && (
            <span
              style={{
                fontSize: '11px',
                padding: '2px 6px',
                borderRadius: '4px',
                backgroundColor: 'var(--bg-tertiary)',
                color: 'var(--warning)'
              }}
              title={skill.unavailableReason ?? ''}
            >
              {t('skillCard.unavailable')}
            </span>
          )}
          {!skill.enabled && (
            <span
              style={{
                fontSize: '11px',
                padding: '2px 6px',
                borderRadius: '4px',
                backgroundColor: 'var(--bg-tertiary)',
                color: 'var(--text-dimmed)'
              }}
            >
              {t('skillCard.disabledBadge')}
            </span>
          )}
        </div>
        <p
          style={{
            margin: '6px 0 0',
            fontSize: '12px',
            color: 'var(--text-secondary)',
            lineHeight: 1.4,
            display: '-webkit-box',
            WebkitLineClamp: 2,
            WebkitBoxOrient: 'vertical',
            overflow: 'hidden'
          }}
        >
          {skill.shortDescription || skill.description || t('skillCard.noDescription')}
        </p>
      </div>
      <div
        style={{ flexShrink: 0, paddingTop: '2px', display: 'flex', alignItems: 'center', gap: '6px' }}
        onClick={handleSwitchClick}
      >
        <span style={{ fontSize: '11px', color: 'var(--text-dimmed)', userSelect: 'none' }}>
          {t('skillCard.on')}
        </span>
        <PillSwitch
          checked={skill.enabled}
          onChange={onToggleEnabled}
          size="sm"
          aria-label={skill.enabled ? t('skillCard.toggleDisable') : t('skillCard.toggleEnable')}
        />
      </div>
    </div>
  )
}

function SourceBadge({
  source,
  t
}: {
  source: SkillEntry['source']
  t: ReturnType<typeof useT>
}): JSX.Element {
  const styles: Record<SkillEntry['source'], React.CSSProperties> = {
    builtin: {
      fontSize: '11px',
      padding: '2px 6px',
      borderRadius: '4px',
      backgroundColor: 'var(--bg-tertiary)',
      color: 'var(--text-dimmed)'
    },
    workspace: {
      fontSize: '11px',
      padding: '2px 6px',
      borderRadius: '4px',
      backgroundColor: 'var(--bg-tertiary)',
      color: 'var(--accent)'
    },
    user: {
      fontSize: '11px',
      padding: '2px 6px',
      borderRadius: '4px',
      backgroundColor: 'rgba(34, 197, 94, 0.12)',
      color: 'var(--success)'
    },
    plugin: {
      fontSize: '11px',
      padding: '2px 6px',
      borderRadius: '4px',
      backgroundColor: 'rgba(14, 165, 233, 0.12)',
      color: '#0ea5e9'
    }
  }
  const labels: Record<SkillEntry['source'], string> = {
    builtin: t('skillCard.builtin'),
    workspace: t('skills.source.workspace'),
    user: t('skills.source.user'),
    plugin: t('plugins.source.plugin')
  }
  return <span style={styles[source]}>{labels[source]}</span>
}

