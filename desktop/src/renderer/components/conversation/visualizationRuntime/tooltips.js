function installTooltips() {
  const gap = 6
  const margin = 4
  const opposite = { top: 'bottom', bottom: 'top', left: 'right', right: 'left' }
  let tooltip = null
  let trigger = null
  let pinned = false
  let previousDescribedBy = null
  const watcher = new MutationObserver(() => {
    if (trigger) show(trigger, pinned)
  })

  const triggerFrom = event => {
    for (const node of event.composedPath()) {
      if (node instanceof Element && node.hasAttribute('data-tooltip')) return node
    }
    return null
  }

  const element = () => {
    if (tooltip?.isConnected) return tooltip
    tooltip = document.createElement('div')
    tooltip.id = 'viz-tooltip'
    tooltip.className = 'tooltip viz-tooltip'
    tooltip.setAttribute('role', 'tooltip')
    tooltip.hidden = true
    document.body.append(tooltip)
    return tooltip
  }

  const clamp = (value, min, max) => Math.max(min, Math.min(value, max))

  const position = () => {
    if (!trigger || !tooltip || tooltip.hidden) return
    if (!trigger.isConnected) {
      hide()
      return
    }
    const anchor = trigger.getBoundingClientRect()
    const width = tooltip.offsetWidth
    const height = tooltip.offsetHeight
    const viewWidth = document.documentElement.clientWidth
    const viewHeight = document.documentElement.clientHeight
    const space = {
      top: anchor.top - margin,
      bottom: viewHeight - anchor.bottom - margin,
      left: anchor.left - margin,
      right: viewWidth - anchor.right - margin
    }
    const need = side => (side === 'top' || side === 'bottom' ? height : width) + gap
    const requested = trigger.getAttribute('data-tooltip-placement')
    const preferred = Object.hasOwn(opposite, requested ?? '') ? requested : 'top'
    const candidates = [preferred, opposite[preferred], ...(preferred === 'bottom' ? ['bottom', 'top'] : ['top', 'bottom'])]
    const side = candidates.find(candidate => space[candidate] >= need(candidate))
      ?? candidates.reduce((best, candidate) => space[candidate] - need(candidate) > space[best] - need(best) ? candidate : best)
    let x = side === 'left' ? anchor.left - gap - width : side === 'right' ? anchor.right + gap : anchor.left + anchor.width / 2 - width / 2
    let y = side === 'top' ? anchor.top - gap - height : side === 'bottom' ? anchor.bottom + gap : anchor.top + anchor.height / 2 - height / 2
    x = clamp(x, margin, viewWidth - width - margin)
    y = clamp(y, margin, viewHeight - height - margin)
    tooltip.style.transform = `translate(${Math.round(x)}px, ${Math.round(y)}px)`
    tooltip.dataset.placement = side
  }

  const restoreDescribedBy = () => {
    if (!trigger || trigger.getRootNode() !== document) return
    if (previousDescribedBy == null) trigger.removeAttribute('aria-describedby')
    else trigger.setAttribute('aria-describedby', previousDescribedBy)
    previousDescribedBy = null
  }

  function show(target, pin) {
    const text = target.getAttribute('data-tooltip')?.trim()
    if (!text) {
      hide()
      return
    }
    const surface = element()
    if (trigger !== target) {
      restoreDescribedBy()
      watcher.disconnect()
      trigger = target
      watcher.observe(target, { attributes: true, attributeFilter: ['data-tooltip', 'data-tooltip-placement'] })
      if (target.getRootNode() === document) {
        previousDescribedBy = target.getAttribute('aria-describedby')
        target.setAttribute('aria-describedby', previousDescribedBy ? `${previousDescribedBy} ${surface.id}` : surface.id)
      }
    }
    pinned = pin
    surface.textContent = text
    surface.hidden = false
    position()
  }

  function hide() {
    watcher.disconnect()
    restoreDescribedBy()
    trigger = null
    pinned = false
    if (tooltip) tooltip.hidden = true
  }

  document.addEventListener('pointerover', event => {
    if (event.pointerType === 'touch') return
    const target = triggerFrom(event)
    if (target) {
      if (target !== trigger) show(target, false)
    } else if (trigger && !pinned) {
      hide()
    }
  })
  document.documentElement.addEventListener('pointerleave', () => {
    if (trigger && !pinned) hide()
  })
  document.addEventListener('pointerdown', event => {
    const target = triggerFrom(event)
    if (event.pointerType !== 'touch') {
      if (!target && pinned) hide()
      return
    }
    if (target && !(target === trigger && pinned)) show(target, true)
    else hide()
  })
  document.addEventListener('focusin', event => {
    const target = triggerFrom(event)
    if (target && target.matches(':focus-visible')) show(target, false)
  })
  document.addEventListener('focusout', event => {
    if (trigger && !pinned && event.composedPath().includes(trigger)) hide()
  })
  document.addEventListener('keydown', event => {
    if (event.key !== 'Escape' || !trigger) return
    event.preventDefault()
    hide()
  })
  addEventListener('scroll', position, true)
  addEventListener('resize', position)
}
