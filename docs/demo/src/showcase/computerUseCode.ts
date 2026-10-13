import type { Action } from './content/computerUse'

/** Element indexes are stable per target so a replay shows the same calls. */
function elementIndex(target: string): number {
  let hash = 0
  for (const char of target) hash = (hash * 31 + char.charCodeAt(0)) % 97
  return hash + 3
}

export function findWindowCode(windowTitle: string): string {
  return [
    'const windows = await dotcraft.computer.list_windows()',
    `const win = windows.find((w) => w.app.displayName === ${JSON.stringify(windowTitle)})`,
    'await dotcraft.computer.get_window_state({ window: win, include_text: true })'
  ].join('\n')
}

/** Each step ends by observing the window, as the computer skill requires after acting. */
export function stepCode(actions: Action[]): string {
  const lines: string[] = []
  for (const action of actions) {
    if ('click' in action) lines.push(`await dotcraft.computer.click({ window: win, element_index: ${elementIndex(action.click)} })`)
    else if ('type' in action) lines.push(`await dotcraft.computer.type_text({ window: win, text: ${JSON.stringify(action.text)} })`)
  }
  lines.push('await dotcraft.computer.get_window_state({ window: win, include_text: true })')
  return lines.join('\n')
}

export function actionsOfStep(actions: Action[], index: number): Action[] {
  const rest = actions.slice(index + 1)
  const next = rest.findIndex((action) => 'step' in action)
  return next === -1 ? rest : rest.slice(0, next)
}
