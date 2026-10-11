function installCarousel(host) {
  const variantsOf = root => [...root.children].filter(child => child.hasAttribute('data-variant'))

  const stepButton = (label, icon) => {
    const button = document.createElement('button')
    button.type = 'button'
    button.className = 'viz-carousel-step'
    button.setAttribute('aria-label', label)
    button.dataset.tooltip = label
    const svg = host.icon(icon, 16)
    if (svg) button.append(svg)
    return button
  }

  const enhance = root => {
    const variants = variantsOf(root)
    if (variants.length === 0 || root.querySelector(':scope > .viz-carousel-controls')) return
    const names = variants.map((variant, index) => variant.getAttribute('data-variant')?.trim() || String(index + 1))
    const controls = document.createElement('div')
    controls.className = 'viz-carousel-controls'
    controls.setAttribute('role', 'group')
    controls.setAttribute('aria-label', root.getAttribute('aria-label') || host.labels.designs)
    const previous = stepButton(root.dataset.previousLabel || host.labels.previousDesign, 'chevron-left')
    const next = stepButton(root.dataset.nextLabel || host.labels.nextDesign, 'chevron-right')
    const picker = document.createElement('select')
    picker.className = 'viz-carousel-picker'
    picker.setAttribute('aria-label', host.labels.design)
    names.forEach((name, index) => picker.add(new Option(name, String(index))))
    const count = document.createElement('span')
    count.className = 'viz-carousel-count'
    count.setAttribute('aria-live', 'polite')
    controls.append(previous, picker, count, next)
    root.append(controls)

    let current = -1
    const show = index => {
      const target = Math.min(variants.length - 1, Math.max(0, index))
      if (target === current) return
      const initial = current < 0
      current = target
      variants.forEach((variant, position) => { variant.hidden = position !== target })
      picker.value = String(target)
      count.textContent = `${target + 1} / ${variants.length}`
      previous.disabled = target === 0
      next.disabled = target === variants.length - 1
      if (document.activeElement === previous && previous.disabled) next.focus()
      if (document.activeElement === next && next.disabled) previous.focus()
      if (!initial) root.dispatchEvent(new CustomEvent('viz:variantchange', { bubbles: true, detail: { variant: names[target] } }))
    }

    previous.addEventListener('click', () => show(current - 1))
    next.addEventListener('click', () => show(current + 1))
    picker.addEventListener('change', () => show(Number(picker.value)))
    controls.addEventListener('keydown', event => {
      if (event.target === picker || event.altKey || event.ctrlKey || event.metaKey) return
      const rtl = getComputedStyle(controls).direction === 'rtl'
      const target = {
        ArrowLeft: current + (rtl ? 1 : -1),
        ArrowRight: current + (rtl ? -1 : 1),
        Home: 0,
        End: variants.length - 1
      }[event.key]
      if (target === undefined) return
      event.preventDefault()
      show(target)
    })
    show(0)
  }

  addEventListener('DOMContentLoaded', () => {
    for (const root of document.querySelectorAll('.viz-carousel')) enhance(root)
  }, { once: true })
}
