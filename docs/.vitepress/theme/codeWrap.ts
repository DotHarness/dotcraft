export function setupCodeWrap(): void {
  window.addEventListener('click', (event) => {
    const target = event.target as Element | null
    const button = target?.closest?.('div[class*="language-"] > button.wrap')
    if (!button) return
    const wrapped = button.getAttribute('aria-pressed') !== 'true'
    button.setAttribute('aria-pressed', String(wrapped))
    button.parentElement?.classList.toggle('is-wrapped', wrapped)
  })
}
