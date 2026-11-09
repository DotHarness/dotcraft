import { useEffect, type RefObject } from 'react'
import { usePluginStore } from '../../stores/pluginStore'
import { useSkillsStore } from '../../stores/skillsStore'
import { SKILL_REF_CLASS } from './richInputConstants'
import { paintSkillRefIcon } from './richInputSerialization'

/** Repaints composer skill pills when the skill or plugin catalog loads after a draft was restored. */
export function useSkillRefIcons(editorRef: RefObject<HTMLElement | null>): void {
  const skills = useSkillsStore((s) => s.skills)
  const plugins = usePluginStore((s) => s.plugins)
  useEffect(() => {
    const pills = editorRef.current?.querySelectorAll<HTMLElement>(`.${SKILL_REF_CLASS}`) ?? []
    for (const pill of pills) {
      const icon = pill.querySelector<HTMLElement>('.dc-ref-icon-default')
      const name = pill.getAttribute('data-skill')
      if (icon && name) paintSkillRefIcon(icon, name)
    }
  }, [editorRef, plugins, skills])
}
