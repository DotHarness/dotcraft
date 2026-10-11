function installTweak(host) {
  const MAX_INSTANCES = 16
  const MAX_CONTROLS = 12
  const MAX_OPTIONS = 12
  const MAX_TEXT = 80
  const REFERENCE = /^[A-Za-z0-9_\-./:@$#]+$/
  const HEX = /^#(?:[0-9a-f]{3}|[0-9a-f]{6})$/i
  const instances = new Map()
  let sequence = 0
  let scheduled = false
  let published = false

  const text = value => typeof value === 'string' ? value.trim().slice(0, MAX_TEXT) : ''
  const reference = value => typeof value === 'string' && value.length <= MAX_TEXT && REFERENCE.test(value) ? value : undefined
  const hex = value => {
    if (typeof value !== 'string' || !HEX.test(value.trim())) return undefined
    const color = value.trim().toLowerCase()
    return color.length === 4 ? '#' + [...color.slice(1)].map(digit => digit + digit).join('') : color
  }
  const variantOf = container => container?.closest('.viz-carousel > [data-variant]') ?? null

  const coerce = (control, value) => {
    if (control.kind === 'slider') {
      const number = Number(value)
      if (typeof value !== 'number' || !Number.isFinite(number)) return undefined
      const snapped = control.min + Math.round((number - control.min) / control.step) * control.step
      return Math.min(control.max, Math.max(control.min, Number(snapped.toFixed(10))))
    }
    if (control.kind === 'color') return hex(value)
    if (control.kind === 'toggle') return typeof value === 'boolean' ? value : undefined
    return control.options.some(option => option.value === value) ? value : undefined
  }

  const snapshot = () => {
    const groups = []
    for (const [id, instance] of instances) {
      if (instance.controls.length === 0) continue
      const variant = variantOf(instance.container)
      groups.push({
        id,
        label: text(instance.container?.getAttribute('aria-label')),
        variant: variant ? text(variant.getAttribute('data-variant')) : null,
        visible: !variant || !variant.hidden,
        controls: instance.controls.map(({ object, property, ...control }) => control)
      })
    }
    const visible = document.querySelector('.viz-carousel > [data-variant]:not([hidden])')
    return { groups, variant: visible ? text(visible.getAttribute('data-variant')) : null }
  }

  const publish = () => {
    if (scheduled) return
    scheduled = true
    queueMicrotask(() => {
      scheduled = false
      published = true
      host.send('visualization/designControls', snapshot())
    })
  }

  const describe = (kind, value, options) => {
    if (kind === 'slider') {
      const min = Number(options.min)
      const max = Number(options.max)
      const step = options.step === undefined ? 1 : Number(options.step)
      if (!Number.isFinite(min) || !Number.isFinite(max) || min >= max || !Number.isFinite(step) || step <= 0) return null
      const control = { kind, min, max, step }
      const unit = text(options.unit).slice(0, 16)
      if (unit) control.unit = unit
      control.initial = coerce(control, value)
      return control.initial === undefined ? null : control
    }
    if (kind === 'select') {
      if (!Array.isArray(options.options)) return null
      const seen = new Set()
      const choices = options.options.slice(0, MAX_OPTIONS).flatMap(option => {
        const value = typeof option === 'string' ? option : option?.value
        if (typeof value !== 'string' || value.length > MAX_TEXT || seen.has(value)) return []
        seen.add(value)
        return [{ label: (typeof option === 'string' ? text(option) : text(option.label)) || value, value }]
      })
      const control = { kind, options: choices, initial: value }
      return choices.length > 0 && coerce(control, value) !== undefined ? control : null
    }
    const initial = kind === 'color' ? hex(value) : typeof value === 'boolean' ? value : undefined
    return initial === undefined ? null : { kind, initial }
  }

  const add = (id, kind, object, property, options) => {
    const instance = instances.get(id)
    if (!instance || instance.controls.length >= MAX_CONTROLS) return
    if (object === null || (typeof object !== 'object' && typeof object !== 'function')) return
    if (typeof property !== 'string' && typeof property !== 'number') return
    const settings = options && typeof options === 'object' ? options : {}
    const control = describe(kind, object[property], settings)
    if (!control) return
    const hint = reference(settings.reference)
    instance.controls.push({
      id: `${id}.${instance.controls.length}`,
      label: text(settings.label) || text(String(property)),
      ...(hint ? { reference: hint } : {}),
      ...control,
      object,
      property
    })
    publish()
  }

  class Tweak {
    #id = null

    constructor(options = {}) {
      if (instances.size >= MAX_INSTANCES) return
      this.#id = `tweak-${++sequence}`
      instances.set(this.#id, {
        container: options?.container instanceof Element ? options.container : null,
        onChange: typeof options?.onChange === 'function' ? options.onChange : null,
        controls: []
      })
    }

    get supported() { return this.#id !== null && instances.has(this.#id) }
    addSlider(object, property, options) { add(this.#id, 'slider', object, property, options); return this }
    addColorPicker(object, property, options) { add(this.#id, 'color', object, property, options); return this }
    addToggle(object, property, options) { add(this.#id, 'toggle', object, property, options); return this }
    addSelect(object, property, options) { add(this.#id, 'select', object, property, options); return this }

    dispose() {
      if (this.#id !== null && instances.delete(this.#id)) publish()
      this.#id = null
    }
  }

  globalThis.Tweak = Tweak

  host.on('visualization/designControls/set', params => {
    const values = params?.values
    if (!values || typeof values !== 'object') return
    const changed = new Set()
    for (const instance of instances.values()) {
      for (const control of instance.controls) {
        if (!Object.hasOwn(values, control.id)) continue
        const value = coerce(control, values[control.id])
        if (value === undefined) continue
        control.object[control.property] = value
        changed.add(instance)
      }
    }
    for (const instance of changed) {
      try {
        instance.onChange?.()
      } catch (error) {
        setTimeout(() => { throw error })
      }
    }
  })

  document.addEventListener('viz:variantchange', () => { if (published) publish() })
}
