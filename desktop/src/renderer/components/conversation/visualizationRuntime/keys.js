function installKeyRelay(host) {
  const relayed = new Set(['Escape', 'ArrowLeft', 'ArrowRight'])
  const editable = 'input, textarea, select, [contenteditable]:not([contenteditable="false"])'

  addEventListener('keydown', event => {
    if (host.displayMode() !== 'fullscreen' || !relayed.has(event.key)) return
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey || event.isComposing) return
    if (event.key !== 'Escape' && event.target instanceof Element && event.target.closest(editable)) return
    setTimeout(() => {
      if (!event.defaultPrevented) host.send('visualization/key', { key: event.key })
    })
  })
}
