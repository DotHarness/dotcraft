function installColorInputs(host) {
  let active = null

  const labelOf = input => (input.getAttribute('aria-label') || input.labels?.[0]?.textContent || input.title || '')
    .replace(/\s+/g, ' ').trim().slice(0, 80)

  const apply = (input, color) => {
    if (typeof color !== 'string' || input.value === color.toLowerCase()) return
    input.value = color
    input.dispatchEvent(new Event('input', { bubbles: true }))
  }

  const open = input => {
    if (active || input.disabled) return
    const rect = input.getBoundingClientRect()
    const initial = input.value
    active = input
    host.request('visualization/pickColor', {
      color: initial,
      label: labelOf(input),
      rect: { x: rect.x, y: rect.y, width: rect.width, height: rect.height }
    })
      .then(result => apply(input, result?.color), () => apply(input, initial))
      .finally(() => {
        active = null
        input.focus({ preventScroll: true })
        if (input.value !== initial) input.dispatchEvent(new Event('change', { bubbles: true }))
      })
  }

  host.on('visualization/colorPreview', params => {
    if (active) apply(active, params?.color)
  })

  addEventListener('click', event => {
    const input = event.target
    if (!(input instanceof HTMLInputElement) || input.type !== 'color') return
    event.preventDefault()
    open(input)
  }, true)

  const showPicker = HTMLInputElement.prototype.showPicker
  HTMLInputElement.prototype.showPicker = function () {
    if (this.type === 'color') open(this)
    else showPicker?.call(this)
  }
}
