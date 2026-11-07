type FakeInputPart = { type: string; text?: string; name?: string; rawText?: string; path?: string }

export function inputText(input: unknown): string {
  return (input as FakeInputPart[]).map((part) => {
    if (part.type === 'text') return part.text ?? ''
    if (part.type === 'commandRef') return part.rawText ?? `/${part.name}`
    if (part.type === 'skillRef') return `$${part.name}`
    if (part.type === 'fileRef') return `@${part.path}`
    return ''
  }).join('')
}
