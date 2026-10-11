function installRangeProgress() {
  const update = input => {
    if (!(input instanceof HTMLInputElement) || input.type !== 'range') return
    const min = Number(input.min || 0)
    const max = Number(input.max || 100)
    const progress = max > min ? (Number(input.value) - min) / (max - min) * 100 : 0
    input.style.setProperty('--viz-range-progress', `${Math.min(100, Math.max(0, progress))}%`)
  }
  const value = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')
  Object.defineProperty(HTMLInputElement.prototype, 'value', {
    ...value,
    set(next) {
      value.set.call(this, next)
      update(this)
    }
  })
  addEventListener('input', event => update(event.target), true)
  new MutationObserver(records => {
    for (const record of records) {
      if (record.type === 'attributes') update(record.target)
      for (const node of record.addedNodes) {
        if (!(node instanceof Element)) continue
        update(node)
        node.querySelectorAll('input[type=range]').forEach(update)
      }
    }
  }).observe(document.documentElement, { subtree: true, childList: true, attributes: true, attributeFilter: ['value', 'min', 'max'] })
}
