export function inSameSideChat(owner: Element | null, target: EventTarget | null): boolean {
  const ownScope = owner?.closest('[data-side-chat]') ?? null
  const targetScope = target instanceof Element ? target.closest('[data-side-chat]') : null
  return ownScope === targetScope
}
