import { useEffect, useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import type { PluginEntry, PluginSkillInfo } from '../../stores/pluginStore'
import { useSkillsStore } from '../../stores/skillsStore'
import { addToast } from '../../stores/toastStore'
import { SkillAvatar } from '../skills/SkillAvatar'
import { PillSwitch } from '../ui/PillSwitch'
import { findPluginSkill } from './pluginCatalogModel'
import styles from './PluginSkillsSection.module.css'

const visibleSkillLimit = 5
const namedSkillLimit = 2
const stackedSkillLimit = 3

export function PluginSkillsSection({ plugin, onOpenSkill }: {
  plugin: PluginEntry
  onOpenSkill: (name: string) => void
}): JSX.Element | null {
  const t = useT()
  const canManage = useConnectionStore((state) => state.capabilities?.skillsManagement === true)
  const { skills, pendingSkillNames, fetchSkills, toggleSkillEnabled } = useSkillsStore()
  const [expanded, setExpanded] = useState(false)

  useEffect(() => {
    if (canManage && plugin.installed && plugin.skills.length > 0) void fetchSkills()
  }, [canManage, fetchSkills, plugin.id, plugin.installed, plugin.enabled, plugin.skills.length])

  useEffect(() => { setExpanded(false) }, [plugin.id])

  if (plugin.skills.length === 0) return null

  const collapsible = plugin.skills.length > visibleSkillLimit
  const visible = collapsible && !expanded ? plugin.skills.slice(0, visibleSkillLimit) : plugin.skills
  const hidden = plugin.skills.slice(visible.length)

  return (
    <section className={styles.section} aria-labelledby="plugin-skills-heading">
      <h2 id="plugin-skills-heading" className={styles.heading}>
        {t('skills.pageTitle')} <span className={styles.count}>{plugin.skills.length}</span>
      </h2>
      {visible.map((skill) => {
        const effectiveSkill = findPluginSkill(skills, plugin.id, skill.name)
        const enabled = plugin.enabled && effectiveSkill?.enabled === true
        const pending = effectiveSkill != null && pendingSkillNames.includes(effectiveSkill.name)
        const title = skillTitle(skill)
        return (
          <div key={skill.name} className={styles.row}>
            <button type="button" className={styles.preview} onClick={() => onOpenSkill(skill.name)}>
              <SkillAvatar size={32} iconDataUrl={skill.iconSmallDataUrl ?? skill.iconLargeDataUrl} />
              <span className={styles.text}>
                <strong className={styles.title}>{title}</strong>
                <span className={styles.description}>{skill.shortDescription || skill.description}</span>
              </span>
            </button>
            {plugin.installed && canManage && (
              <PillSwitch
                checked={enabled}
                disabled={!plugin.enabled || !effectiveSkill || pending}
                aria-busy={pending}
                aria-label={t('skillCard.toggleLabel', { name: title })}
                size="sm"
                onChange={(nextEnabled) => {
                  if (!effectiveSkill) return
                  void toggleSkillEnabled(effectiveSkill.name, nextEnabled)
                    .catch(() => addToast(t('skills.updateFailed'), 'error'))
                }}
              />
            )}
          </div>
        )
      })}
      {collapsible && (
        <button
          type="button"
          className={styles.disclosure}
          aria-expanded={expanded}
          onClick={() => setExpanded((open) => !open)}
        >
          {!expanded && (
            <span className={styles.stack} aria-hidden>
              {hidden.slice(0, stackedSkillLimit).map((skill) => (
                <SkillAvatar
                  key={skill.name}
                  size={16}
                  role="compact"
                  framed
                  iconDataUrl={skill.iconSmallDataUrl ?? skill.iconLargeDataUrl}
                />
              ))}
            </span>
          )}
          <span className={styles.disclosureLabel}>{disclosureLabel(hidden, expanded, t)}</span>
        </button>
      )}
    </section>
  )
}

function skillTitle(skill: PluginSkillInfo): string {
  return skill.displayName || skill.name
}

function disclosureLabel(
  hidden: PluginSkillInfo[],
  expanded: boolean,
  t: (key: string, vars?: Record<string, string | number>) => string,
): string {
  if (expanded) return t('plugins.detail.skills.showLess')
  const names = hidden.slice(0, namedSkillLimit).map(skillTitle).join(', ')
  const rest = hidden.length - namedSkillLimit
  if (rest <= 0) return t('plugins.detail.skills.showMore', { names })
  return t(rest === 1 ? 'plugins.detail.skills.showMoreRest.one' : 'plugins.detail.skills.showMoreRest.other', {
    names,
    count: rest,
  })
}
