function installCalendar(host) {
  if (customElements.get('viz-calendar')) return
  const timePattern = /^(?:([01]\d|2[0-3]):([0-5]\d)|24:00)$/
  const datePattern = /^(\d{4})-(\d{2})-(\d{2})$/
  const tones = new Set(['blue', 'green', 'red', 'orange', 'purple', 'yellow'])
  const maxEvents = 200
  const minHeight = 22
  const shortHeight = 40
  const laneGap = 8
  const styles = `
    :host { display: block; min-width: 0; padding: 12px; border: 1px solid var(--border); border-radius: 8px; color: var(--card-foreground); background: var(--card); font-size: var(--font-size-base); line-height: 1.35; container-type: inline-size; }
    :host([hidden]) { display: none; }
    * { box-sizing: border-box; }
    .header { display: flex; align-items: baseline; justify-content: space-between; gap: 8px; margin: 0 0 8px; }
    .date { margin: 0; font-size: inherit; font-weight: 500; }
    .zone, .hour, .meta { color: var(--muted-foreground); font-size: max(11px, calc(var(--font-size-base) - 2px)); }
    .grid { display: grid; grid-template-columns: 48px minmax(0, 1fr); padding-block: 8px; }
    .hours, .track { position: relative; }
    .hour { position: absolute; right: 8px; transform: translateY(-50%); font-variant-numeric: tabular-nums; white-space: nowrap; }
    .line { position: absolute; right: 0; left: 0; border-top: 1px solid var(--border); }
    .event { position: absolute; display: flex; min-width: 0; flex-direction: column; align-items: stretch; gap: 1px; margin: 0; padding: 4px 8px; overflow: hidden; border: 0; border-radius: 8px; color: var(--foreground); background: color-mix(in srgb, var(--tone) 24%, var(--background)); font: inherit; text-align: start; cursor: default; }
    .event:hover { box-shadow: inset 0 0 0 100vmax color-mix(in srgb, var(--foreground) 3%, transparent); }
    .event:focus-visible { z-index: 2; outline: 2px solid var(--ring); outline-offset: -2px; }
    .event.past { color: color-mix(in srgb, var(--foreground) 55%, var(--background)); background: color-mix(in srgb, var(--tone) 12%, var(--background)); }
    .event.past .meta { color: color-mix(in srgb, var(--muted-foreground) 70%, var(--background)); }
    :host([interactive]) .event { cursor: var(--cursor-interaction); }
    .title { display: flex; min-width: 0; align-items: center; gap: 4px; font-weight: 500; }
    .title span { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .title svg { flex: none; color: currentColor; opacity: 0.7; }
    .meta { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-variant-numeric: tabular-nums; }
    .now { position: absolute; z-index: 3; right: 0; left: -4px; border-top: 2px solid var(--red); pointer-events: none; }
    .now::before { position: absolute; top: -5px; left: 0; width: 8px; height: 8px; border-radius: 50%; background: var(--red); content: ""; }
    .empty { position: absolute; inset: 0; display: grid; place-items: center; color: var(--muted-foreground); }
    .error { margin: 0; color: var(--destructive); }
    @container (max-width: 360px) {
      .grid { grid-template-columns: 40px minmax(0, 1fr); }
      .hour { right: 6px; }
      .event { padding-inline: 6px; }
    }
  `

  const toMinutes = value => {
    if (typeof value !== 'string' || !timePattern.test(value)) return null
    if (value === '24:00') return 1440
    const [hours, minutes] = value.split(':').map(Number)
    return hours * 60 + minutes
  }

  const clockFormat = (locale, options) => new Intl.DateTimeFormat(locale, { ...options, timeZone: 'UTC' })
  const clock = (format, minutes) => format.format(Date.UTC(1970, 0, 1, 0, minutes))

  const create = (tag, className, text) => {
    const node = document.createElement(tag)
    if (className) node.className = className
    if (text != null) node.textContent = text
    return node
  }

  const assignLanes = events => {
    let group = []
    let groupEnd = -1
    const close = () => {
      const laneEnds = []
      for (const event of group) {
        let lane = laneEnds.findIndex(end => end <= event.from)
        if (lane === -1) lane = laneEnds.push(event.to) - 1
        else laneEnds[lane] = event.to
        event.lane = lane
      }
      for (const event of group) event.lanes = laneEnds.length
    }
    for (const event of events) {
      if (group.length && event.from >= groupEnd) {
        close()
        group = []
      }
      group.push(event)
      groupEnd = Math.max(groupEnd, event.to)
    }
    if (group.length) close()
  }

  class VizCalendar extends HTMLElement {
    static observedAttributes = ['date', 'start', 'end', 'now', 'time-zone', 'lang', 'empty-label', 'interactive', 'events']
    #events = undefined

    constructor() {
      super()
      if (!this.shadowRoot) this.attachShadow({ mode: 'open' })
    }

    get events() {
      return this.#events
    }

    set events(value) {
      this.#events = value
      this.#render()
    }

    connectedCallback() {
      if (Object.prototype.hasOwnProperty.call(this, 'events')) {
        const value = this.events
        delete this.events
        this.#events = value
      }
      this.#render()
    }

    attributeChangedCallback() {
      this.#render()
    }

    #read() {
      const date = datePattern.exec(this.getAttribute('date') ?? '')
      const day = date ? new Date(Date.UTC(Number(date[1]), Number(date[2]) - 1, Number(date[3]))) : null
      if (!day || day.getUTCDate() !== Number(date[3])) throw new Error('Set date to a real day as YYYY-MM-DD.')
      const start = toMinutes(this.getAttribute('start') ?? '09:00')
      const end = toMinutes(this.getAttribute('end') ?? '17:00')
      if (start == null || end == null || end <= start) throw new Error('Set start and end as HH:MM, with end after start.')
      const nowText = this.getAttribute('now')
      const now = nowText == null ? null : toMinutes(nowText)
      if (nowText != null && now == null) throw new Error('Set now as HH:MM.')
      let source = this.#events
      if (source === undefined) {
        const json = this.getAttribute('events')
        try {
          source = json == null || json.trim() === '' ? [] : JSON.parse(json)
        } catch {
          throw new Error('The events attribute is not valid JSON.')
        }
      }
      if (!Array.isArray(source)) throw new Error('Events must be an array.')
      if (source.length > maxEvents) throw new Error(`Supply at most ${maxEvents} events.`)
      const events = source.map((event, index) => {
        if (!event || typeof event !== 'object') throw new Error(`Event ${index + 1} must be an object.`)
        const title = typeof event.title === 'string' ? event.title.trim() : ''
        const from = toMinutes(event.start)
        const to = toMinutes(event.end)
        if (!title) throw new Error(`Event ${index + 1} needs a title.`)
        if (from == null || to == null || to <= from) throw new Error(`Event ${index + 1} needs start and end as HH:MM, with end after start.`)
        return {
          source: event,
          index,
          title,
          from,
          to,
          detail: typeof event.detail === 'string' ? event.detail.trim() : '',
          video: event.video === true,
          tone: tones.has(event.tone) ? event.tone : 'blue'
        }
      })
      return { day, start, end, now, events }
    }

    #render() {
      if (!this.isConnected || !this.shadowRoot) return
      const root = this.shadowRoot
      const style = create('style', '', styles)
      let data
      try {
        data = this.#read()
      } catch (error) {
        const message = create('p', 'error', error instanceof Error ? error.message : String(error))
        message.setAttribute('role', 'alert')
        root.replaceChildren(style, message)
        return
      }
      const locale = this.getAttribute('lang') || document.documentElement.lang || undefined
      const dateLabel = new Intl.DateTimeFormat(locale, { weekday: 'long', month: 'long', day: 'numeric', timeZone: 'UTC' }).format(data.day)
      const hourFormat = clockFormat(locale, { hour: 'numeric' })
      const timeFormat = clockFormat(locale, { hour: 'numeric', minute: '2-digit' })
      const range = data.end - data.start
      const hourHeight = range <= 240 ? 56 : range <= 480 ? 44 : 36
      const scale = hourHeight / 60

      const header = create('div', 'header')
      header.append(create('h3', 'date', dateLabel))
      const zone = this.getAttribute('time-zone')
      if (zone) header.append(create('span', 'zone', zone))

      const grid = create('div', 'grid')
      grid.setAttribute('role', 'group')
      grid.setAttribute('aria-label', dateLabel)
      const hours = create('div', 'hours')
      const track = create('div', 'track')
      hours.setAttribute('aria-hidden', 'true')
      for (const box of [hours, track]) box.style.height = `${Math.round(range * scale)}px`
      for (let minute = Math.ceil(data.start / 60) * 60; minute <= data.end; minute += 60) {
        const top = `${Math.round((minute - data.start) * scale)}px`
        const label = create('span', 'hour', clock(hourFormat, minute))
        label.style.top = top
        hours.append(label)
        const line = create('div', 'line')
        line.style.top = top
        line.setAttribute('aria-hidden', 'true')
        track.append(line)
      }

      const visible = data.events
        .filter(event => event.to > data.start && event.from < data.end)
        .map(event => ({ ...event, from: Math.max(event.from, data.start), to: Math.min(event.to, data.end), shownFrom: event.from, shownTo: event.to }))
        .sort((left, right) => left.from - right.from || right.to - left.to || left.index - right.index)
      assignLanes(visible)
      const interactive = this.hasAttribute('interactive')
      for (const event of visible) {
        const time = `${clock(timeFormat, event.shownFrom)} – ${clock(timeFormat, event.shownTo)}`
        const top = Math.round((event.from - data.start) * scale) + 1
        const height = Math.max(minHeight, Math.round((event.to - event.from) * scale) - 2)
        const button = create('button', data.now != null && event.shownTo <= data.now ? 'event past' : 'event')
        button.type = 'button'
        button.style.cssText = `--tone: var(--${event.tone}); top: ${top}px; height: ${height}px; left: calc(${event.lane} * (100% + ${laneGap}px) / ${event.lanes}); width: calc((100% + ${laneGap}px) / ${event.lanes} - ${laneGap}px);`
        button.setAttribute('data-tooltip', [event.title, time, event.detail].filter(Boolean).join('\n'))
        button.setAttribute('aria-label', [event.title, time, event.detail].filter(Boolean).join(', '))
        const title = create('span', 'title')
        title.append(create('span', '', event.title))
        if (event.video) {
          const icon = host.icon('video', 12)
          if (icon) title.append(icon)
        }
        button.append(title)
        if (height >= shortHeight) button.append(create('span', 'meta', time))
        if (event.detail && height >= shortHeight + 18) button.append(create('span', 'meta', event.detail))
        if (interactive) {
          button.addEventListener('click', () => this.dispatchEvent(new CustomEvent('eventselect', {
            bubbles: true,
            composed: true,
            detail: { event: event.source, index: event.index }
          })))
        }
        track.append(button)
      }
      if (visible.length === 0) track.append(create('div', 'empty', this.getAttribute('empty-label') || 'No events'))
      if (data.now != null && data.now >= data.start && data.now <= data.end) {
        const marker = create('div', 'now')
        marker.style.top = `${Math.round((data.now - data.start) * scale)}px`
        marker.setAttribute('aria-hidden', 'true')
        track.append(marker)
      }
      grid.append(hours, track)
      root.replaceChildren(style, header, grid)
    }
  }

  customElements.define('viz-calendar', VizCalendar)
}
