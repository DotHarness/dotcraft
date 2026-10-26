import { Box } from 'lucide-react'
import type { JSX, ReactNode } from 'react'
import { translate, type AppLocale } from '../../../shared/locales'
import { openSkill, openSkillFile } from '../../utils/openSkill'

const SKILL_NAME_TOKEN = '__DOTCRAFT_SKILL_NAME__'

/** Renders a skill tool label with the skill name as an inline reference chip. */
export function renderSkillToolLabel(
  locale: AppLocale,
  labelKey: string,
  name: string,
  file?: string
): ReactNode {
  const template = translate(locale, labelKey, { name: SKILL_NAME_TOKEN })
  const parts = template.split(SKILL_NAME_TOKEN)
  if (parts.length === 1) return translate(locale, labelKey, { name })

  return (
    <>
      {parts.map((part, index) => (
        <span key={`${part}-${index}`}>
          {part}
          {index < parts.length - 1 && <SkillRef name={name} file={file} />}
        </span>
      ))}
    </>
  )
}

export function SkillRef({ name, file }: { name: string; file?: string }): JSX.Element {
  return (
    <button
      type="button"
      className="dc-ref dc-ref-skill"
      // The chip sits inside a `<summary>`, whose click would otherwise toggle the row.
      onClick={(event) => {
        event.preventDefault()
        event.stopPropagation()
        if (file) void openSkillFile(name, file)
        else openSkill(name)
      }}
    >
      <Box size={12} strokeWidth={2.25} aria-hidden />
      <span>{name}</span>
    </button>
  )
}
