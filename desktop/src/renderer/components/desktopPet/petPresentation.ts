/** True while the companion window presents the active thread on the desktop. */
export function isDesktopPetPresenting(): boolean {
  return document.documentElement.hasAttribute('data-desktop-pet-detached')
}

export function onDesktopPetPresentationChange(listener: (presenting: boolean) => void): () => void {
  return window.api?.desktopPet?.onEvent?.((event) => {
    if (event.type === 'ownership') listener(event.detached)
  }) ?? (() => {})
}
