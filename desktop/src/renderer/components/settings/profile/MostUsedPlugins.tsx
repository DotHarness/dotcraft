import { useEffect, type JSX } from 'react'
import type { MessageKey } from '../../../../shared/locales'
import { usePluginStore, type PluginEntry } from '../../../stores/pluginStore'
import type { SkillUsageWire } from '../../../stores/profileStore'
import { useSkillsStore, type SkillEntry } from '../../../stores/skillsStore'
import { useUIStore } from '../../../stores/uiStore'
import { pluginIconUrl } from '../../plugins/PluginCatalogItem'
import { ExtensionsIcon } from '../../ui/AppIcons'
import { IdentityMark } from '../../ui/IdentityMark'
import { IdentityMarkFallback } from '../../ui/IdentityMarkFallback'
import styles from './ProfileInsights.module.css'

type TFn = (key: MessageKey | string, vars?: Record<string, string | number>) => string

export function MostUsedPlugins({
  skills,
  t
}: {
  skills: SkillUsageWire[]
  t: TFn
}): JSX.Element {
  const plugins = usePluginStore((s) => s.plugins)
  const catalogSkills = useSkillsStore((s) => s.skills)
  const hasUsage = skills.length > 0

  useEffect(() => {
    if (!hasUsage) return
    const store = useSkillsStore.getState()
    if (store.skills.length === 0 && !store.loading) void store.fetchSkills()
  }, [hasUsage])

  return (
    <section className={styles.column}>
      <h2 className={styles.heading}>{t('settings.profile.plugins.title')}</h2>
      {hasUsage ? (
        <ul className={styles.list}>
          {skills.map((skill) => (
            <li key={skill.name} className={styles.row}>
              <span className={styles.identity}>
                <IdentityMark
                  role="compact"
                  src={usageIconUrl(skill, plugins, catalogSkills)}
                  fallback={<IdentityMarkFallback kind="skill" />}
                />
                <span title={`$${skill.name}`} className={styles.name}>
                  ${skill.name}
                </span>
              </span>
              <span className={styles.count}>
                {t('settings.profile.plugins.runs', { count: skill.count })}
              </span>
            </li>
          ))}
        </ul>
      ) : (
        <div className={styles.empty}>
          <ExtensionsIcon size={14} />
          <span>{t('settings.profile.plugins.empty')}</span>
          <button type="button" className={styles.browse} onClick={browsePlugins}>
            {t('settings.profile.plugins.browse')}
          </button>
        </div>
      )}
    </section>
  )
}

function browsePlugins(): void {
  usePluginStore.getState().clearSelection()
  const ui = useUIStore.getState()
  ui.setPluginCatalogSurface('plugins')
  ui.setActiveMainView('skills')
}

function usageIconUrl(
  usage: SkillUsageWire,
  plugins: PluginEntry[],
  skills: SkillEntry[]
): string | null {
  const plugin = usage.pluginId ? plugins.find((entry) => entry.id === usage.pluginId) : undefined
  const skill = skills.find((entry) => entry.name === usage.name)
  return (plugin && pluginIconUrl(plugin)) || skill?.iconSmallDataUrl || skill?.iconLargeDataUrl || null
}
