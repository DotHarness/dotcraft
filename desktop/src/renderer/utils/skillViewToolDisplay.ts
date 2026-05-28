import { translate, type AppLocale } from '../../shared/locales'

export const SKILL_VIEW_TOOL_NAME = 'SkillView'

export interface SkillViewDisplay {
  name: string
  loaded: boolean
  message: string
}

function readName(args: Record<string, unknown> | undefined): string {
  return typeof args?.name === 'string' ? args.name.trim() : ''
}

export function getSkillViewDisplay(
  args: Record<string, unknown> | undefined,
  resultText: string | undefined
): SkillViewDisplay {
  const name = readName(args)
  const result = (resultText ?? '').trim()
  const missingName = result === 'Skill name is required.'
  const notFound = /^Skill '.+' not found\.$/.test(result)

  return {
    name,
    loaded: !!name && !!result && !missingName && !notFound,
    message: missingName || notFound ? result : ''
  }
}

export function formatSkillViewLabel(
  args: Record<string, unknown> | undefined,
  locale: AppLocale
): string {
  const name = readName(args) || translate(locale, 'skillManage.tool.skillFallback')
  return translate(locale, 'skillView.tool.loadedSkill', { name })
}

export function formatSkillViewRunningLabel(
  args: Record<string, unknown> | undefined,
  locale: AppLocale
): string {
  const name = readName(args) || translate(locale, 'skillManage.tool.skillFallback')
  return translate(locale, 'skillView.tool.loadingSkill', { name })
}
