export function observeDesktopPetOwnership(): () => void {
  return window.api?.desktopPet?.onEvent(event => {
    if (event.type === 'ownership') {
      document.documentElement.toggleAttribute('data-desktop-pet-detached', event.detached)
    } else if (event.type === 'return-seat' && !document.querySelector('[data-pet-owner]')) {
      const mascot = Array.from(document.querySelectorAll<HTMLElement>('.composer-mascot-jelly'))
        .find(element => element.getBoundingClientRect().width > 0)
      const rect = mascot?.getBoundingClientRect()
      void window.api.desktopPet.command({ type: 'seat', seat: rect
        ? { x: rect.x, y: rect.y, width: rect.width, height: rect.height } : null })
    }
  }) ?? (() => {})
}
