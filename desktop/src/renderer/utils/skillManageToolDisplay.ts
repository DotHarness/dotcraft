import { translate, type AppLocale } from '../../shared/locales'
import type { FileDiff } from '../types/toolCall'
import { computeDiffHunks } from './diffExtractor'

export const SKILL_MANAGE_TOOL_NAME = 'SkillManage'

export type SkillManageAction = 'create' | 'edit' | 'patch' | 'write_file' | 'remove_file' | 'delete'

export interface SkillManageResult {
  success: boolean
  message: string
  path?: string | null
  error?: string | null
  replacementCount?: number | null
}

export interface SkillManageDisplay {
  action: SkillManageAction | null
  name: string
  result: SkillManageResult | null
  message: string
  variantUpdated: boolean
}

function normalizeAction(value: unknown): SkillManageAction | null {
  if (typeof value !== 'string') return null
  const action = value.trim().toLowerCase()
  if (
    action === 'create'
    || action === 'edit'
    || action === 'patch'
    || action === 'write_file'
    || action === 'remove_file'
    || action === 'delete'
  ) {
    return action
  }
  return null
}

function readString(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

export function parseSkillManageResult(resultText: string | undefined): SkillManageResult | null {
  if (!resultText) return null
  try {
    const parsed = JSON.parse(resultText) as Record<string, unknown>
    if (typeof parsed !== 'object' || parsed == null) return null
    return {
      success: parsed.success === true,
      message: readString(parsed.message),
      path: typeof parsed.path === 'string' ? parsed.path : null,
      error: typeof parsed.error === 'string' ? parsed.error : null,
      replacementCount: typeof parsed.replacementCount === 'number'
        ? parsed.replacementCount
        : null
    }
  } catch {
    return null
  }
}

export function getSkillManageDisplay(
  args: Record<string, unknown> | undefined,
  resultText: string | undefined
): SkillManageDisplay {
  const result = parseSkillManageResult(resultText)
  const message = result?.error || result?.message || ''
  return {
    action: normalizeAction(args?.action),
    name: readString(args?.name).trim(),
    result,
    message,
    variantUpdated: /\boriginal skill was not modified\b/i.test(message)
  }
}

export function formatSkillManageLabel(
  args: Record<string, unknown> | undefined,
  resultText: string | undefined,
  locale: AppLocale
): string {
  const display = getSkillManageDisplay(args, resultText)
  const name = display.name || 'SkillManage'
  const filePath = readString(args?.filePath)
  switch (display.action) {
    case 'create':
      return translate(locale, 'skillManage.tool.createdSkill', { name })
    case 'edit':
      return translate(locale, 'skillManage.tool.updatedSkill', { name })
    case 'patch':
      return translate(locale, 'skillManage.tool.patchedSkill', { name })
    case 'write_file':
      return filePath
        ? translate(locale, 'skillManage.tool.addedFile', { filePath })
        : translate(locale, 'skillManage.tool.addedSkillFile', { name })
    case 'remove_file':
      return filePath
        ? translate(locale, 'skillManage.tool.removedFile', { filePath })
        : translate(locale, 'skillManage.tool.removedSkillFile', { name })
    case 'delete':
      return translate(locale, 'skillManage.tool.deletedSkill', { name })
    default:
      return translate(locale, 'toolCall.called', { toolName: SKILL_MANAGE_TOOL_NAME })
  }
}

export function formatSkillManageRunningLabel(
  args: Record<string, unknown> | undefined,
  locale: AppLocale
): string {
  const action = normalizeAction(args?.action)
  const name = readString(args?.name).trim()
  const vars = { name: name || translate(locale, 'skillManage.tool.skillFallback') }
  switch (action) {
    case 'create':
      return translate(locale, 'skillManage.tool.creatingSkill', vars)
    case 'edit':
      return translate(locale, 'skillManage.tool.updatingSkill', vars)
    case 'patch':
      return translate(locale, 'skillManage.tool.patchingSkill', vars)
    case 'write_file':
      return translate(locale, 'skillManage.tool.addingFile', vars)
    case 'remove_file':
      return translate(locale, 'skillManage.tool.removingFile', vars)
    case 'delete':
      return translate(locale, 'skillManage.tool.deletingSkill', vars)
    default:
      return translate(locale, 'toolCall.streaming.genericBuiltin', { toolName: SKILL_MANAGE_TOOL_NAME })
  }
}

function buildFileDiff(
  filePath: string,
  turnId: string,
  originalContent: string,
  currentContent: string,
  isNewFile: boolean
): FileDiff {
  const { hunks, additions, deletions } = computeDiffHunks(originalContent, currentContent)
  return {
    filePath,
    turnId,
    turnIds: [turnId],
    additions,
    deletions,
    diffHunks: hunks,
    status: 'written',
    isNewFile,
    originalContent,
    currentContent
  }
}

export function buildSkillManageDiff(
  args: Record<string, unknown> | undefined,
  resultText: string | undefined,
  turnId: string
): FileDiff | null {
  const display = getSkillManageDisplay(args, resultText)
  if (display.result && display.result.success !== true) return null
  if (!display.name) return null

  const skillRoot = display.name
  switch (display.action) {
    case 'create': {
      const content = readString(args?.content)
      if (!content) return null
      return buildFileDiff(`${skillRoot}/SKILL.md`, turnId, '', content, true)
    }
    case 'edit': {
      const content = readString(args?.content)
      if (!content) return null
      return buildFileDiff(`${skillRoot}/SKILL.md`, turnId, '', content, true)
    }
    case 'patch': {
      const oldString = readString(args?.oldString)
      const newString = readString(args?.newString)
      if (!oldString && !newString) return null
      const filePath = readString(args?.filePath) || 'SKILL.md'
      return buildFileDiff(`${skillRoot}/${filePath}`, turnId, oldString, newString, false)
    }
    case 'write_file': {
      const filePath = readString(args?.filePath)
      const fileContent = readString(args?.fileContent)
      if (!filePath) return null
      return buildFileDiff(`${skillRoot}/${filePath}`, turnId, '', fileContent, true)
    }
    default:
      return null
  }
}

export function shouldRenderSkillManageCard(
  args: Record<string, unknown> | undefined,
  resultText: string | undefined
): boolean {
  const display = getSkillManageDisplay(args, resultText)
  if (!display.result?.success || !display.name) return false
  return display.action === 'create'
    || display.action === 'edit'
    || display.action === 'patch'
    || display.action === 'write_file'
}
