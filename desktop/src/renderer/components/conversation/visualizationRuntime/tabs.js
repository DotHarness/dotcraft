function installTabs() {
  const tabSelector = '.nav [role="tab"]'
  const isDisabled = tab => tab.disabled === true || tab.getAttribute('aria-disabled') === 'true'

  const tabFrom = event => {
    for (const node of event.composedPath()) {
      if (node instanceof Element && node.matches(tabSelector)) return node
    }
    return null
  }

  const tabsOf = tab => {
    const list = tab.closest('[role="tablist"]')
    if (!list) return [tab]
    return [...list.querySelectorAll('[role="tab"]')].filter(item => item.closest('[role="tablist"]') === list)
  }

  const panelOf = tab => {
    const id = tab.getAttribute('aria-controls')
    return id ? tab.getRootNode().getElementById?.(id) ?? null : null
  }

  const select = tab => {
    const tabs = tabsOf(tab)
    const panel = panelOf(tab)
    for (const item of tabs) {
      const selected = item === tab
      item.setAttribute('aria-selected', String(selected))
      item.classList.toggle('active', selected)
      item.tabIndex = selected ? 0 : -1
      const itemPanel = panelOf(item)
      if (itemPanel && itemPanel !== panel) itemPanel.hidden = true
    }
    if (!panel) return
    panel.hidden = false
    if (tab.id) panel.setAttribute('aria-labelledby', tab.id)
  }

  document.addEventListener('click', event => {
    if (event.defaultPrevented) return
    const tab = tabFrom(event)
    if (tab && !isDisabled(tab)) select(tab)
  })

  document.addEventListener('keydown', event => {
    const tab = tabFrom(event)
    if (!tab || event.altKey || event.ctrlKey || event.metaKey) return
    const tabs = tabsOf(tab).filter(item => !isDisabled(item) || item === tab)
    const enabled = tabs.filter(item => !isDisabled(item))
    if (enabled.length === 0) return
    const rtl = getComputedStyle(tab).direction === 'rtl'
    const index = tabs.indexOf(tab)
    const step = direction => {
      for (let offset = 1; offset <= tabs.length; offset++) {
        const candidate = tabs[((index + direction * offset) % tabs.length + tabs.length) % tabs.length]
        if (!isDisabled(candidate)) return candidate
      }
      return tab
    }
    const next = {
      ArrowRight: () => step(rtl ? -1 : 1),
      ArrowLeft: () => step(rtl ? 1 : -1),
      ArrowDown: () => step(1),
      ArrowUp: () => step(-1),
      Home: () => enabled[0],
      End: () => enabled[enabled.length - 1]
    }[event.key]?.()
    if (!next) return
    event.preventDefault()
    next.focus()
    if (next !== tab || tab.getAttribute('aria-selected') !== 'true') next.click()
  })

  addEventListener('DOMContentLoaded', () => {
    for (const list of document.querySelectorAll('.nav[role="tablist"]')) {
      const tabs = [...list.querySelectorAll('[role="tab"]')].filter(item => item.closest('[role="tablist"]') === list)
      const initial = tabs.find(item => item.getAttribute('aria-selected') === 'true' || item.classList.contains('active'))
        ?? tabs.find(item => !isDisabled(item))
      if (initial) select(initial)
    }
  }, { once: true })
}
