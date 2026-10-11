import { createContext, useCallback, useContext, useEffect, useRef, useState } from 'react'
import type { GenerativeUiJson } from './generativeUiProtocol'
import type { GenerativeUiCallback } from './generativeUiSchema'

export interface GenerativeUiRenderContextValue {
  invoke: (callback: GenerativeUiCallback | undefined, args: GenerativeUiJson[]) => void
  inert: boolean
}

export const GenerativeUiRenderContext = createContext<GenerativeUiRenderContextValue>({ invoke: () => {}, inert: false })

export function useGenerativeUiInvoke(): GenerativeUiRenderContextValue['invoke'] {
  return useContext(GenerativeUiRenderContext).invoke
}

export function useGenerativeUiInert(): boolean {
  return useContext(GenerativeUiRenderContext).inert
}

export interface GenerativeUiFormContextValue {
  setValue: (name: string, value: GenerativeUiJson) => void
  deleteValue: (name: string) => void
}

export const GenerativeUiFormContext = createContext<GenerativeUiFormContextValue | null>(null)

/**
 * A field owns its draft while its own edits are still in flight, so a snapshot that echoes an older
 * value never rewinds what the reader is typing; a value the program changed on its own is adopted.
 */
export function useGenerativeUiFieldValue<T extends GenerativeUiJson>(options: {
  value: T | undefined
  defaultValue: T | undefined
  fallback: T
  name: string | undefined
  onChange: GenerativeUiCallback | undefined
}): [T, (next: T) => void] {
  const { value, defaultValue, fallback, name, onChange } = options
  const invoke = useGenerativeUiInvoke()
  const form = useContext(GenerativeUiFormContext)
  const [draft, setDraft] = useState<T>(value ?? defaultValue ?? fallback)
  const pending = useRef<T[]>([])

  useEffect(() => {
    if (value === undefined) return
    const index = pending.current.findIndex(sent => Object.is(sent, value))
    if (index >= 0) {
      pending.current = pending.current.slice(index + 1)
      if (pending.current.length > 0) return
    } else {
      pending.current = []
    }
    setDraft(value)
  }, [value])

  useEffect(() => {
    if (!name || !form) return
    return () => form.deleteValue(name)
  }, [form, name])

  useEffect(() => {
    if (name) form?.setValue(name, draft)
  }, [draft, form, name])

  const change = useCallback((next: T): void => {
    setDraft(next)
    if (!onChange) return
    if (value !== undefined) pending.current = [...pending.current, next].slice(-32)
    invoke(onChange, [next])
  }, [invoke, onChange, value])

  return [draft, change]
}
