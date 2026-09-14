const KEY = 'dotcraft:composer-text-drafts:v1'

function readDrafts(): Record<string, string> {
  try { return JSON.parse(localStorage.getItem(KEY) ?? '{}') } catch { return {} }
}

export function readPlainComposerDraft(key: string): string {
  const value = readDrafts()[key]
  return typeof value === 'string' ? value : ''
}

export function savePlainComposerDraft(key: string, text: string): void {
  try {
    const drafts = readDrafts()
    if (text.trim()) drafts[key] = text
    else delete drafts[key]
    localStorage.setItem(KEY, JSON.stringify(drafts))
  } catch { /* Draft persistence may be unavailable in ephemeral windows. */ }
}
