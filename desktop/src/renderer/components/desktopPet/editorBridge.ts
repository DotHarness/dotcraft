import { useEffect, type RefObject } from 'react'

export interface PetEditor {
  getText: () => string
  setText: (text: string) => void
  submit: () => void
  enabled: boolean
}
const editors = new WeakMap<HTMLElement, PetEditor>()

export function findPetEditor(root: HTMLElement): PetEditor | undefined {
  const element = root.querySelector<HTMLElement>('.rich-input-area')
  return element ? editors.get(element) : undefined
}

export function usePetEditorBridge(ref: RefObject<HTMLDivElement | null>, editor: PetEditor): void {
  useEffect(() => {
    const element = ref.current
    if (!element) return
    editors.set(element, editor)
    return () => { editors.delete(element) }
  }, [ref, editor.getText, editor.setText, editor.submit, editor.enabled])
}
