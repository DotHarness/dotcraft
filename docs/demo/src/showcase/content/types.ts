export type Lang = 'en' | 'zh'

export interface Exchange {
  prompt: string
  answer: string
}

export interface ComparedExchange {
  prompt: string
  plain: string
  interactive: string
}

export type Localized<T> = Record<Lang, T>

export function uiBlock(strings: unknown, program: string): string {
  return ['```dotcraft-ui', `const T = ${JSON.stringify(strings)}`, '', program.trim(), '```'].join('\n')
}

export function visualizationReference(file: string, title: string): string {
  return `visualize${JSON.stringify({ path: `/home/you/projects/notes/.craft/visualizations/${file}`, title })}`
}
