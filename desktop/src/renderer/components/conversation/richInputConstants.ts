export const FILE_REF_CLASS = 'dc-file-ref'
export const COMMAND_REF_CLASS = 'dc-command-ref'
export const SKILL_REF_CLASS = 'dc-skill-ref'
export const THREAD_REF_CLASS = 'dc-thread-ref'

/** Custom clipboard MIME used to round-trip rich refs in composer. */
export const RICH_REFS_CLIPBOARD_MIME = 'application/x-dotcraft-refs'

const REF_CLASSES = [FILE_REF_CLASS, COMMAND_REF_CLASS, SKILL_REF_CLASS, THREAD_REF_CLASS]
export const REF_SELECTOR = REF_CLASSES.map((refClass) => `.${refClass}`).join(', ')

export function isRefElement(node: Node | null): boolean {
  return node instanceof HTMLElement && REF_CLASSES.some((refClass) => node.classList.contains(refClass))
}
