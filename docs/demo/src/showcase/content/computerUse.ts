import { mailExample } from './computerUseMail'
import { formExample } from './computerUseForm'
import { sheetExample } from './computerUseSheet'
import type { Lang, Localized } from './types'

export type ComputerUseAppKind = 'sheet' | 'form' | 'mail'

/** Targets name elements in the simulated app by `data-cu-target`; `done` marks a task complete for the stop report. */
export type Action =
  | { step: string }
  | { move: string }
  | { click: string }
  | { type: string; text: string }
  | { set: Record<string, string> }
  | { done: number }
  | { pause: number }

export interface ComputerUseScript {
  title: string
  prompt: string
  app: {
    kind: ComputerUseAppKind
    name: string
    /** What the approval shows as its target: the app's executable path or package id. */
    target: string
    window: string
    /** False when the app is already always allowed, so no approval appears. */
    approval: boolean
  }
  labels: Record<string, string>
  values: Record<string, string>
  tasks: string[]
  findStep: string
  actions: Action[]
  reply: string
  declined: string
  confirm?: {
    question: string
    yes: { label: string; message: string }
    no: { label: string; message: string; reply: string }
    actions: Action[]
    reply: string
  }
}

export const computerUseExamples: Record<string, Localized<ComputerUseScript>> = {
  'cu-sheet': sheetExample,
  'cu-form': formExample,
  'cu-mail': mailExample
}

export const DEFAULT_COMPUTER_USE_EXAMPLE = 'cu-sheet'

const copy = {
  en: {
    stopped: (app: string, done: string[], remaining: string[]) => [
      `I stopped when you pressed Esc and won't use ${app} again in this turn.`,
      '',
      '**Done**',
      ...(done.length > 0 ? done.map((task) => `- ${task}`) : [`- Nothing yet. ${app} is unchanged.`]),
      '',
      '**Not done yet**',
      ...remaining.map((task) => `- ${task}`),
      '',
      'Tell me when you want me to pick up from here.'
    ].join('\n'),
    replyLabel: 'Your reply',
    alwaysAllowed: (app: string) => `In DotCraft, ${app} now appears under Settings → Computer use → Always-allowed apps.`,
    stopHint: 'Press Esc or click the bar to stop'
  },
  zh: {
    stopped: (app: string, done: string[], remaining: string[]) => [
      `你按了 Esc，我已经停下，这一轮不会再操作 ${app}。`,
      '',
      '**已完成**',
      ...(done.length > 0 ? done.map((task) => `- ${task}`) : [`- 还没有改动，${app} 保持原样。`]),
      '',
      '**还没做**',
      ...remaining.map((task) => `- ${task}`),
      '',
      '需要我接着做时告诉我。'
    ].join('\n'),
    replyLabel: '你的回复',
    alwaysAllowed: (app: string) => `在 DotCraft 中，${app} 现在会出现在“设置 → 电脑操控 → 始终允许的应用”里。`,
    stopHint: '按 Esc 或点击状态条即可停止'
  }
} satisfies Record<Lang, unknown>

export function computerUseCopy(lang: Lang): (typeof copy)['en'] {
  return copy[lang]
}
