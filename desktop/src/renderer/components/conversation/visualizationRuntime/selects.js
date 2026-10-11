function installSelectMenus(host) {
  const MAX_OPTIONS = 500
  const MAX_TEXT = 200
  let active = null

  const isPopup = select => select instanceof HTMLSelectElement && !select.multiple && select.size <= 1
  const clean = (value, limit) => String(value ?? '').replace(/\s+/g, ' ').trim().slice(0, limit)
  const shown = select => [...select.options].filter(option => !option.hidden).slice(0, MAX_OPTIONS)
  const disabled = option => option.disabled || (option.parentElement instanceof HTMLOptGroupElement && option.parentElement.disabled)

  const choose = (select, option) => {
    if (!option || option.index === select.selectedIndex) return
    select.selectedIndex = option.index
    select.dispatchEvent(new Event('input', { bubbles: true }))
    select.dispatchEvent(new Event('change', { bubbles: true }))
  }

  const open = select => {
    if (active || select.disabled) return
    const options = shown(select)
    if (options.length === 0) return
    const rect = select.getBoundingClientRect()
    active = select
    host.request('visualization/selectMenu', {
      label: clean(select.getAttribute('aria-label') || select.labels?.[0]?.textContent || select.title, 80),
      options: options.map(option => ({
        label: clean(option.label || option.text, MAX_TEXT),
        disabled: disabled(option),
        ...(option.parentElement instanceof HTMLOptGroupElement ? { group: clean(option.parentElement.label, MAX_TEXT) } : {})
      })),
      selectedIndex: options.findIndex(option => option.index === select.selectedIndex),
      rect: { x: rect.x, y: rect.y, width: rect.width, height: rect.height }
    })
      .then(result => choose(select, options[result?.index]), () => {})
      .finally(() => {
        active = null
        select.focus({ preventScroll: true })
      })
  }

  const step = (select, key) => {
    const options = shown(select).filter(option => !disabled(option))
    const current = options.findIndex(option => option.index === select.selectedIndex)
    const next = key === 'Home' ? 0
      : key === 'End' ? options.length - 1
        : key === 'ArrowDown' ? Math.min(options.length - 1, current + 1)
          : Math.max(0, current - 1)
    choose(select, options[next])
  }

  addEventListener('mousedown', event => {
    const select = event.target instanceof Element ? event.target.closest('select') : null
    if (event.button !== 0 || !isPopup(select)) return
    event.preventDefault()
    select.focus({ preventScroll: true })
    open(select)
  }, true)

  addEventListener('click', event => {
    if (event.target instanceof Element && isPopup(event.target.closest('select'))) event.preventDefault()
  }, true)

  addEventListener('keydown', event => {
    const select = event.target
    if (!isPopup(select) || event.ctrlKey || event.metaKey) return
    const arrow = event.key === 'ArrowDown' || event.key === 'ArrowUp'
    if (event.key === ' ' || event.key === 'Enter' || event.key === 'F4' || (event.altKey && arrow)) {
      event.preventDefault()
      open(select)
    } else if (!event.altKey && (arrow || event.key === 'Home' || event.key === 'End')) {
      event.preventDefault()
      step(select, event.key)
    }
  }, true)

  const showPicker = HTMLSelectElement.prototype.showPicker
  HTMLSelectElement.prototype.showPicker = function () {
    if (isPopup(this)) open(this)
    else showPicker?.call(this)
  }
}
