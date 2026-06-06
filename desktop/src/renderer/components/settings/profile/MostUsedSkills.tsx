import type { CSSProperties, JSX } from 'react'
import type { MessageKey } from '../../../../shared/locales'
import type { SkillUsageWire } from '../../../stores/profileStore'

type TFn = (key: MessageKey | string, vars?: Record<string, string | number>) => string

/**
 * Right "Most used skills" column of the Profile page (spec §27A.5): skills ranked by run
 * count, each shown by name with a plugin badge when the skill comes from a plugin.
 * Forward-only, so the list is empty until skills are referenced after the feature ships.
 */
export function MostUsedSkills({
  skills,
  t
}: {
  skills: SkillUsageWire[]
  t: TFn
}): JSX.Element {
  return (
    <section style={{ display: 'flex', flexDirection: 'column', gap: '12px', minWidth: 0 }}>
      <div style={headingStyle}>{t('settings.profile.skills.title')}</div>
      {skills.length === 0 ? (
        <div style={emptyStyle}>{t('settings.profile.skills.empty')}</div>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '10px' }}>
          {skills.map((skill) => (
            <div key={skill.name} style={rowStyle}>
              <span style={nameWrapStyle}>
                <span title={skill.name} style={nameStyle}>{skill.name}</span>
                {skill.pluginDisplayName ? (
                  <span title={skill.pluginDisplayName} style={badgeStyle}>
                    {skill.pluginDisplayName}
                  </span>
                ) : null}
              </span>
              <span style={countStyle}>{t('settings.profile.skills.runs', { count: skill.count })}</span>
            </div>
          ))}
        </div>
      )}
    </section>
  )
}

const headingStyle: CSSProperties = {
  fontSize: '15px',
  fontWeight: 600,
  color: 'var(--text-primary)'
}

const emptyStyle: CSSProperties = {
  fontSize: '12px',
  color: 'var(--text-dimmed)',
  lineHeight: 1.5
}

const rowStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'baseline',
  justifyContent: 'space-between',
  gap: '12px'
}

const nameWrapStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  minWidth: 0
}

const nameStyle: CSSProperties = {
  fontSize: '13px',
  fontWeight: 500,
  color: 'var(--text-primary)',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const badgeStyle: CSSProperties = {
  flexShrink: 0,
  fontSize: '11px',
  color: 'var(--text-secondary)',
  background: 'var(--bg-secondary)',
  border: '1px solid var(--border-default)',
  borderRadius: '999px',
  padding: '1px 8px',
  maxWidth: '140px',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const countStyle: CSSProperties = {
  fontSize: '13px',
  color: 'var(--text-dimmed)',
  flexShrink: 0
}
