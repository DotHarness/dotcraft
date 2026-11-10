import type { WebContents } from 'electron'

const pending = new WeakMap<WebContents, number>()

export async function withAutomationInput<T>(page: WebContents, run: () => Promise<T>): Promise<T> {
  pending.set(page, (pending.get(page) ?? 0) + 1)
  try {
    return await run()
  } finally {
    const count = (pending.get(page) ?? 1) - 1
    if (count > 0) pending.set(page, count)
    else pending.delete(page)
  }
}

export function isAutomationInput(page: WebContents): boolean {
  return pending.has(page)
}
