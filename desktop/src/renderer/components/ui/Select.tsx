import {
  Check,
  ChevronDown
} from 'lucide-react'
import {
  useCallback,
  useEffect,
  useId,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type HTMLAttributes,
  type JSX,
  type KeyboardEvent,
  type ReactNode
} from 'react'
import { createPortal } from 'react-dom'
import { ActionTooltip } from './ActionTooltip'

export interface SelectOption<T extends string = string> {
  value: T
  label: ReactNode
  description?: ReactNode
  icon?: ReactNode
  tooltip?: string
  disabled?: boolean
}

export type SelectAppearance = 'field' | 'frameless'

export interface SelectProps<T extends string = string> {
  id?: string
  value: T
  options: ReadonlyArray<SelectOption<T>>
  onValueChange: (value: T) => void | boolean | Promise<void | boolean>
  onBeforeOpen?: () => void | boolean | Promise<void | boolean>
  ariaLabel?: string
  disabled?: boolean
  style?: CSSProperties
  valueProps?: HTMLAttributes<HTMLSpanElement>
  menuMaxHeight?: number
  appearance?: SelectAppearance
  /** Text-only field selects expand to reveal their longest option by default. */
  adaptiveWidth?: boolean
}

interface MenuPosition {
  top: number
  left: number
  width: number
  maxHeight: number
}

type AdaptiveAnchor = 'left' | 'right'

interface AdaptiveMetrics {
  target: number
  anchor: AdaptiveAnchor
  edge: number
}

const VIEWPORT_INSET = 8
const MENU_GAP = 6
const MENU_MIN_WIDTH = 160
const MENU_CHROME_WIDTH = 72
const EXPAND_DURATION_MS = 180

export function Select<T extends string = string>({
  id,
  value,
  options,
  onValueChange,
  onBeforeOpen,
  ariaLabel,
  disabled = false,
  style,
  valueProps,
  menuMaxHeight = 280,
  appearance = 'field',
  adaptiveWidth
}: SelectProps<T>): JSX.Element {
  const reactId = useId()
  const listboxId = `${id ?? reactId}-listbox`
  const triggerRef = useRef<HTMLButtonElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const measureRef = useRef<HTMLSpanElement>(null)
  const openingRef = useRef(false)
  const closedWidthRef = useRef(0)
  const adaptiveTargetRef = useRef<number | null>(null)
  const adaptiveAnchorRef = useRef<AdaptiveAnchor>('left')
  const adaptiveEdgeRef = useRef<number | null>(null)
  const revealFallbackRef = useRef<number | null>(null)
  const [open, setOpen] = useState(false)
  const [position, setPosition] = useState<MenuPosition | null>(null)
  const [closedWidth, setClosedWidth] = useState<number | null>(null)
  const [expandedWidth, setExpandedWidth] = useState<number | null>(null)
  const [adaptiveAnchor, setAdaptiveAnchor] = useState<AdaptiveAnchor>('left')
  const [menuReady, setMenuReady] = useState(true)
  const selectedIndex = options.findIndex((option) => option.value === value)
  const selectedOption = selectedIndex >= 0 ? options[selectedIndex] : options[0]
  const selectedEnabledIndex = selectedIndex >= 0 && !options[selectedIndex]?.disabled
    ? selectedIndex
    : firstEnabledIndex(options)
  const [activeIndex, setActiveIndex] = useState(selectedEnabledIndex)
  const textLabels = options.map((option) => typeof option.label === 'string' ? option.label : null)
  const standardOptions = textLabels.every((label) => label !== null)
    && options.every((option) => !option.icon && !option.description)
  const adaptive = (adaptiveWidth ?? appearance !== 'frameless') && standardOptions
  const measurementKey = textLabels.join('\u0000')

  const activeOptionId = open && activeIndex >= 0 ? `${listboxId}-option-${activeIndex}` : undefined

  const applyValueChange = useCallback((nextValue: string): boolean | Promise<boolean> => {
    const nextOption = options.find((option) => option.value === nextValue)
    if (!nextOption || nextOption.disabled) return false

    try {
      const result = onValueChange(nextOption.value)
      if (result instanceof Promise) {
        return result.then((applied) => applied !== false).catch(() => false)
      }
      return result !== false
    } catch {
      return false
    }
  }, [onValueChange, options])

  const cancelMenuReveal = useCallback(() => {
    if (revealFallbackRef.current != null) {
      window.clearTimeout(revealFallbackRef.current)
      revealFallbackRef.current = null
    }
  }, [])

  const measureAdaptiveTarget = useCallback((): AdaptiveMetrics | null => {
    const trigger = triggerRef.current
    if (!trigger || !adaptive) return null

    const rect = trigger.getBoundingClientRect()
    if (closedWidthRef.current <= 0 && rect.width > 0) {
      closedWidthRef.current = rect.width
      setClosedWidth(rect.width)
    }
    const baseWidth = closedWidthRef.current || rect.width
    const longestLabel = Math.max(
      0,
      ...Array.from(measureRef.current?.children ?? []).map((node) =>
        (node as HTMLElement).getBoundingClientRect().width)
    )
    const anchor = resolveAdaptiveAnchor(trigger)
    const edge = anchor === 'left'
      ? Math.max(VIEWPORT_INSET, rect.left)
      : Math.min(window.innerWidth - VIEWPORT_INSET, rect.right)
    const availableWidth = Math.max(
      baseWidth,
      anchor === 'left'
        ? window.innerWidth - VIEWPORT_INSET - edge
        : edge - VIEWPORT_INSET
    )
    const preferredWidth = Math.max(
      baseWidth,
      MENU_MIN_WIDTH,
      Math.ceil(longestLabel + MENU_CHROME_WIDTH)
    )
    return {
      target: Math.min(preferredWidth, availableWidth),
      anchor,
      edge
    }
  }, [adaptive])

  const updatePosition = useCallback(() => {
    const trigger = triggerRef.current
    if (!trigger) return
    const rect = trigger.getBoundingClientRect()
    const availableBelow = Math.max(120, window.innerHeight - rect.bottom - VIEWPORT_INSET)
    const availableAbove = Math.max(120, rect.top - VIEWPORT_INSET)
    const opensUp = availableBelow < 180 && availableAbove > availableBelow
    const maxHeight = Math.min(menuMaxHeight, opensUp ? availableAbove : availableBelow)
    const menu = menuRef.current
    const measuredHeight = menu
      ? Math.min(
          maxHeight,
          menu.scrollHeight || menu.getBoundingClientRect().height || maxHeight
        )
      : maxHeight
    const top = opensUp
      ? Math.max(VIEWPORT_INSET, rect.top - measuredHeight - MENU_GAP)
      : Math.min(window.innerHeight - VIEWPORT_INSET, rect.bottom + MENU_GAP)
    const width = adaptive && adaptiveTargetRef.current != null
      ? adaptiveTargetRef.current
      : Math.max(rect.width, MENU_MIN_WIDTH)
    const left = adaptive && adaptiveEdgeRef.current != null
      ? adaptiveAnchorRef.current === 'left'
        ? Math.max(
            VIEWPORT_INSET,
            Math.min(adaptiveEdgeRef.current, window.innerWidth - width - VIEWPORT_INSET)
          )
        : Math.max(
            VIEWPORT_INSET,
            Math.min(
              adaptiveEdgeRef.current - width,
              window.innerWidth - width - VIEWPORT_INSET
            )
          )
      : Math.min(
          Math.max(VIEWPORT_INSET, rect.left),
          Math.max(VIEWPORT_INSET, window.innerWidth - width - VIEWPORT_INSET)
        )
    const nextPosition = { top, left, width, maxHeight }
    setPosition((current) => {
      if (
        current &&
        current.top === nextPosition.top &&
        current.left === nextPosition.left &&
        current.width === nextPosition.width &&
        current.maxHeight === nextPosition.maxHeight
      ) {
        return current
      }
      return nextPosition
    })
  }, [adaptive, menuMaxHeight])

  useLayoutEffect(() => {
    if (!adaptive) {
      closedWidthRef.current = 0
      adaptiveTargetRef.current = null
      adaptiveEdgeRef.current = null
      setClosedWidth(null)
      setExpandedWidth(null)
      setMenuReady(true)
      return
    }
    const trigger = triggerRef.current
    const width = trigger?.getBoundingClientRect().width ?? 0
    if (width > 0 && closedWidthRef.current <= 0) {
      closedWidthRef.current = width
      setClosedWidth(width)
    }
    if (trigger) {
      const anchor = resolveAdaptiveAnchor(trigger)
      adaptiveAnchorRef.current = anchor
      setAdaptiveAnchor(anchor)
    }
  }, [adaptive])

  useLayoutEffect(() => {
    if (!open) return
    updatePosition()
  }, [open, updatePosition])

  useLayoutEffect(() => {
    if (!open || !position) return
    updatePosition()
  }, [open, options, position, updatePosition])

  useLayoutEffect(() => {
    if (!open || !adaptive) return
    const metrics = measureAdaptiveTarget()
    if (!metrics) return
    adaptiveTargetRef.current = metrics.target
    adaptiveAnchorRef.current = metrics.anchor
    adaptiveEdgeRef.current = metrics.edge
    setAdaptiveAnchor(metrics.anchor)
    setExpandedWidth(metrics.target)
    updatePosition()
  }, [adaptive, measureAdaptiveTarget, measurementKey, open, updatePosition])

  useEffect(() => {
    if (!open || !adaptive) return
    const trigger = triggerRef.current
    const target = adaptiveTargetRef.current
    if (!trigger || target == null) return

    cancelMenuReveal()
    setMenuReady(false)
    const reducedMotionSetting = document.documentElement.getAttribute('data-reduce-motion')
    const reducedMotion = reducedMotionSetting === 'on'
      || (
        reducedMotionSetting !== 'off'
        && typeof window.matchMedia === 'function'
        && window.matchMedia('(prefers-reduced-motion: reduce)').matches
      )
    if (reducedMotion || Math.abs(target - closedWidthRef.current) < 1) {
      setMenuReady(true)
      return
    }

    const handleTransitionEnd = (event: TransitionEvent): void => {
      if (event.propertyName !== 'width') return
      cancelMenuReveal()
      setMenuReady(true)
    }
    trigger.addEventListener('transitionend', handleTransitionEnd)
    revealFallbackRef.current = window.setTimeout(() => {
      revealFallbackRef.current = null
      setMenuReady(true)
    }, EXPAND_DURATION_MS + 40)
    return () => {
      trigger.removeEventListener('transitionend', handleTransitionEnd)
      cancelMenuReveal()
    }
  }, [adaptive, cancelMenuReveal, expandedWidth, open])

  useEffect(() => {
    if (!open) return

    function handlePointerDown(event: MouseEvent): void {
      const target = event.target as Node
      if (triggerRef.current?.contains(target) || menuRef.current?.contains(target)) return
      closeMenu()
    }

    document.addEventListener('mousedown', handlePointerDown)
    window.addEventListener('resize', updatePosition)
    window.addEventListener('scroll', updatePosition, true)
    return () => {
      document.removeEventListener('mousedown', handlePointerDown)
      window.removeEventListener('resize', updatePosition)
      window.removeEventListener('scroll', updatePosition, true)
    }
  }, [open, updatePosition])

  useEffect(() => {
    if (open) return
    setActiveIndex(selectedEnabledIndex)
  }, [open, selectedEnabledIndex])

  function openMenu(): void {
    if (disabled || openingRef.current) return
    const completeOpen = (): void => {
      setActiveIndex(selectedEnabledIndex)
      if (adaptive) {
        const metrics = measureAdaptiveTarget()
        if (metrics) {
          adaptiveTargetRef.current = metrics.target
          adaptiveAnchorRef.current = metrics.anchor
          adaptiveEdgeRef.current = metrics.edge
          setAdaptiveAnchor(metrics.anchor)
          setExpandedWidth(metrics.target)
        }
        setMenuReady(false)
      } else {
        setMenuReady(true)
      }
      setOpen(true)
    }
    if (!onBeforeOpen) {
      completeOpen()
      return
    }
    openingRef.current = true
    try {
      const result = onBeforeOpen()
      if (result instanceof Promise) {
        void result
          .then((allowed) => { if (allowed !== false) completeOpen() })
          .catch(() => {})
          .finally(() => { openingRef.current = false })
      } else {
        openingRef.current = false
        if (result !== false) completeOpen()
      }
    } catch {
      openingRef.current = false
    }
  }

  function closeMenu(): void {
    cancelMenuReveal()
    setOpen(false)
    setMenuReady(false)
  }

  function selectOption(index: number): void {
    const option = options[index]
    if (!option || option.disabled) return

    const completeSelection = (): void => {
      closeMenu()
      requestAnimationFrame(() => triggerRef.current?.focus())
    }
    const applied = applyValueChange(option.value)
    if (applied instanceof Promise) {
      void applied.then((success) => {
        if (success) completeSelection()
      })
    } else if (applied) {
      completeSelection()
    }
  }

  function moveActive(delta: 1 | -1): void {
    const next = nextEnabledIndex(options, activeIndex, delta)
    if (next >= 0) setActiveIndex(next)
  }

  function handleKeyDown(event: KeyboardEvent<HTMLButtonElement>): void {
    if (disabled) return

    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault()
        if (!open) {
          openMenu()
        } else {
          moveActive(1)
        }
        break
      case 'ArrowUp':
        event.preventDefault()
        if (!open) {
          openMenu()
        } else {
          moveActive(-1)
        }
        break
      case 'Home':
        if (!open) return
        event.preventDefault()
        setActiveIndex(firstEnabledIndex(options))
        break
      case 'End':
        if (!open) return
        event.preventDefault()
        setActiveIndex(lastEnabledIndex(options))
        break
      case 'Enter':
      case ' ':
        event.preventDefault()
        if (!open) {
          openMenu()
        } else {
          selectOption(activeIndex)
        }
        break
      case 'Escape':
        if (!open) return
        event.preventDefault()
        event.stopPropagation()
        closeMenu()
        break
    }
  }

  const menu = useMemo(() => {
    if (!open || !position) return null
    return createPortal(
      <div
        ref={menuRef}
        id={listboxId}
        role="listbox"
        aria-label={ariaLabel}
        className="dc-settings-select-menu"
        data-adaptive-select={adaptive ? 'true' : undefined}
        data-adaptive-select-anchor={adaptive ? adaptiveAnchor : undefined}
        data-adaptive-select-ready={adaptive ? String(menuReady) : undefined}
        style={{
          top: position.top,
          left: position.left,
          width: position.width,
          maxHeight: position.maxHeight
        }}
      >
        {options.map((option, index) => {
          const selected = option.value === value
          const active = index === activeIndex
          return (
            <div
              key={option.value}
              id={`${listboxId}-option-${index}`}
              role="option"
              aria-selected={selected}
              aria-disabled={option.disabled || undefined}
              // Keeps the underlying value observable in the DOM, the way a native
              // <option value> was, since the trigger only renders the label.
              data-value={option.value}
              data-active={active || undefined}
              data-disabled={option.disabled || undefined}
              className="dc-settings-select-option"
              onMouseDown={(event) => event.preventDefault()}
              onMouseEnter={() => {
                if (!option.disabled) setActiveIndex(index)
              }}
              onClick={() => selectOption(index)}
            >
              {option.icon && (
                <span className="dc-settings-select-option__icon" aria-hidden="true">
                  {option.icon}
                </span>
              )}
              {withOptionTooltip(option, adaptive)}
              <span className="dc-settings-select-option__check" aria-hidden="true">
                {selected && <Check size={15} strokeWidth={1.8} />}
              </span>
            </div>
          )
        })}
      </div>,
      document.body
    )
  }, [activeIndex, adaptive, adaptiveAnchor, applyValueChange, ariaLabel, listboxId, menuReady, open, options, position, value])

  const triggerWidth = adaptive
    ? open && expandedWidth != null
      ? expandedWidth
      : closedWidth ?? style?.width
    : style?.width
  const triggerStyle: CSSProperties = {
    ...style,
    ...(triggerWidth != null ? { width: triggerWidth } : {})
  }

  const trigger = (
    <button
      ref={triggerRef}
      id={id}
      type="button"
      role="combobox"
      value={value}
      aria-label={ariaLabel}
      aria-haspopup="listbox"
      aria-expanded={open}
      aria-controls={open ? listboxId : undefined}
      aria-activedescendant={activeOptionId}
      disabled={disabled}
      data-open={open || undefined}
      data-disabled={disabled || undefined}
      data-appearance={appearance}
      className="dc-settings-select"
      onClick={() => {
        if (open) {
          closeMenu()
        } else {
          openMenu()
        }
      }}
      onKeyDown={handleKeyDown}
      style={triggerStyle}
    >
      <span className="dc-settings-select__value" {...valueProps}>
        {selectedOption?.label ?? value}
      </span>
      <ChevronDown
        size={15}
        strokeWidth={1.8}
        className="dc-settings-select__chevron"
        aria-hidden="true"
      />
    </button>
  )

  const renderedTrigger = selectedOption?.tooltip && !adaptive
        ? (
            <ActionTooltip label={selectedOption.tooltip} placement="top" multiline>
              {trigger}
            </ActionTooltip>
          )
        : trigger

  if (!adaptive) {
    return <>{renderedTrigger}{menu}</>
  }

  return (
    <>
      <span
        className="dc-adaptive-select"
        data-adaptive="true"
        data-adaptive-anchor={adaptiveAnchor}
        data-open={open || undefined}
        style={{
          width: closedWidth ?? style?.width,
          maxWidth: style?.maxWidth ?? '100%'
        }}
      >
        {renderedTrigger}
        <span ref={measureRef} className="dc-adaptive-select__measure" aria-hidden="true">
          {textLabels.map((label, index) => <span key={`${label}-${index}`}>{label}</span>)}
        </span>
      </span>
      {menu}
    </>
  )
}

function withOptionTooltip<T extends string>(option: SelectOption<T>, adaptive: boolean): JSX.Element {
  const copy = (
    <span className="dc-settings-select-option__copy">
      <span className="dc-settings-select-option__label">{option.label}</span>
      {option.description && (
        <span className="dc-settings-select-option__description">
          {option.description}
        </span>
      )}
    </span>
  )
  if (!option.tooltip || adaptive) return copy
  return (
    <ActionTooltip
      label={option.tooltip}
      placement="right"
      multiline
      wrapperStyle={{ flex: '1 1 auto', minWidth: 0, width: '100%' }}
    >
      {copy}
    </ActionTooltip>
  )
}

function resolveAdaptiveAnchor(trigger: HTMLElement): AdaptiveAnchor {
  const wrapper = trigger.closest<HTMLElement>('.dc-adaptive-select')
  const row = trigger.closest<HTMLElement>('.dc-settings-row')
  if (!wrapper || !row) return 'left'

  const wrapperRect = wrapper.getBoundingClientRect()
  const rowRect = row.getBoundingClientRect()
  const leftGap = wrapperRect.left - rowRect.left
  const rightGap = rowRect.right - wrapperRect.right
  return rightGap < leftGap ? 'right' : 'left'
}

function firstEnabledIndex<T extends string>(options: ReadonlyArray<SelectOption<T>>): number {
  return options.findIndex((option) => !option.disabled)
}

function lastEnabledIndex<T extends string>(options: ReadonlyArray<SelectOption<T>>): number {
  for (let index = options.length - 1; index >= 0; index -= 1) {
    if (!options[index].disabled) return index
  }
  return -1
}

function nextEnabledIndex<T extends string>(
  options: ReadonlyArray<SelectOption<T>>,
  currentIndex: number,
  delta: 1 | -1
): number {
  if (options.length === 0) return -1
  const fallback = delta === 1 ? firstEnabledIndex(options) : lastEnabledIndex(options)
  if (fallback < 0) return -1

  let index = currentIndex
  for (let visited = 0; visited < options.length; visited += 1) {
    index = (index + delta + options.length) % options.length
    if (!options[index].disabled) return index
  }
  return fallback
}
