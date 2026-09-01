import { Sparkle } from 'lucide-react'
import type { JSX, ReactNode } from 'react'
import { translate, type AppLocale } from '../../../shared/locales'
import { usePluginStore } from '../../stores/pluginStore'
import { useSkillsStore } from '../../stores/skillsStore'
import { useUIStore } from '../../stores/uiStore'

const SKILL_NAME_TOKEN = '__DOTCRAFT_SKILL_NAME__'

/** Renders a skill tool label with the skill name as an inline reference chip. */
export function renderSkillToolLabel(
  locale: AppLocale,
  labelKey: string,
  name: string,
  extraVars?: Record<string, string>
): ReactNode {
  const template = translate(locale, labelKey, { ...extraVars, name: SKILL_NAME_TOKEN })
  const parts = template.split(SKILL_NAME_TOKEN)
  if (parts.length === 1) return translate(locale, labelKey, { ...extraVars, name })

  return (
    <>
      {parts.map((part, index) => (
        <span key={`${part}-${index}`}>
          {part}
          {index < parts.length - 1 && <SkillRef name={name} />}
        </span>
      ))}
    </>
  )
}

export function SkillRef({ name }: { name: string }): JSX.Element {
  const openSkill = async (): Promise<void> => {
    usePluginStore.getState().clearSelection()
    const ui = useUIStore.getState()
    ui.setPluginCatalogSurface('skills')
    ui.setActiveMainView('skills')
    const skills = useSkillsStore.getState()
    await skills.fetchSkills()
    await skills.selectSkill(name)
  }

  return (
    <button
      type="button"
      className="dc-ref dc-ref-skill"
      // The chip sits inside a `<summary>`, whose click would otherwise toggle the row.
      onClick={(event) => {
        event.preventDefault()
        event.stopPropagation()
        void openSkill()
      }}
    >
      <Sparkle size={12} strokeWidth={2.25} aria-hidden />
      <span>{name}</span>
    </button>
  )
}
