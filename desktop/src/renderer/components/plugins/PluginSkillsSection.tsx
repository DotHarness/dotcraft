import { useEffect } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import type { PluginEntry } from '../../stores/pluginStore'
import { useSkillsStore } from '../../stores/skillsStore'
import { addToast } from '../../stores/toastStore'
import { SkillAvatar } from '../skills/SkillAvatar'
import { PillSwitch } from '../ui/PillSwitch'
import { findPluginSkill } from './pluginCatalogModel'
import styles from './PluginSkillsSection.module.css'

export function PluginSkillsSection({ plugin, onOpenSkill }: {
  plugin: PluginEntry
  onOpenSkill: (name: string) => void
}): JSX.Element | null {
  const t = useT()
  const canManage = useConnectionStore((state) => state.capabilities?.skillsManagement === true)
  const { skills, pendingSkillNames, fetchSkills, toggleSkillEnabled } = useSkillsStore()

  useEffect(() => {
    if (canManage && plugin.installed && plugin.skills.length > 0) void fetchSkills()
  }, [canManage, fetchSkills, plugin.id, plugin.installed, plugin.enabled, plugin.skills.length])

  if (plugin.skills.length === 0) return null

  return (
    <section className={styles.section} aria-labelledby="plugin-skills-heading">
      <h2 id="plugin-skills-heading" className={styles.heading}>
        {t('skills.pageTitle')} <span className={styles.count}>{plugin.skills.length}</span>
      </h2>
      {plugin.skills.map((skill) => {
        const effectiveSkill = findPluginSkill(skills, plugin.id, skill.name)
        const enabled = plugin.enabled && effectiveSkill?.enabled === true
        const pending = effectiveSkill != null && pendingSkillNames.includes(effectiveSkill.name)
        const title = skill.displayName || skill.name
        return (
          <div key={skill.name} className={styles.row}>
            <button type="button" className={styles.preview} onClick={() => onOpenSkill(skill.name)}>
              <SkillAvatar
                name={skill.name}
                displayName={title}
                size={32}
                iconDataUrl={skill.iconSmallDataUrl ?? skill.iconLargeDataUrl}
              />
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
    </section>
  )
}
