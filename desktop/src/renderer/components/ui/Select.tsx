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

export interface SelectOption<T extends string = string> {
  value: T
  label: ReactNode
  description?: ReactNode
  icon?: ReactNode
  disabled?: boolean
}

export type SelectAppearance = 'field' | 'frameless'

export interface SelectProps<T extends string = string> {
  id?: string
  value: T
  options: ReadonlyArray<SelectOption<T>>
  onValueChange: (value: T) => void | boolean | Promise<void | boolean>
  ariaLabel?: string
  disabled?: boolean
  style?: CSSProperties
  valueProps?: HTMLAttributes<HTMLSpanElement>
  menuMaxHeight?: number
  appearance?: SelectAppearance
}

interface MenuPosition {
  top: number
  left: number
  width: number
  maxHeight: number
}

export function Select<T extends string = string>({
  id,
  value,
  options,
  onValueChange,
  ariaLabel,
  disabled = false,
  style,
  valueProps,
  menuMaxHeight = 280,
  appearance = 'field'
}: SelectProps<T>): JSX.Element {
  const reactId = useId()
  const listboxId = `${id ?? reactId}-listbox`
  const triggerRef = useRef<HTMLButtonElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [position, setPosition] = useState<MenuPosition | null>(null)
  const selectedIndex = options.findIndex((option) => option.value === value)
  const selectedOption = selectedIndex >= 0 ? options[selectedIndex] : options[0]
  const selectedEnabledIndex = selectedIndex >= 0 && !options[selectedIndex]?.disabled
    ? selectedIndex
    : firstEnabledIndex(options)
  const [activeIndex, setActiveIndex] = useState(selectedEnabledIndex)

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

  const updatePosition = useCallback(() => {
    const trigger = triggerRef.current
    if (!trigger) return
    const rect = trigger.getBoundingClientRect()
    const viewportPadding = 8
    const menuGap = 6
    const availableBelow = Math.max(120, window.innerHeight - rect.bottom - viewportPadding)
    const availableAbove = Math.max(120, rect.top - viewportPadding)
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
      ? Math.max(viewportPadding, rect.top - measuredHeight - menuGap)
      : Math.min(window.innerHeight - viewportPadding, rect.bottom + menuGap)
    const width = Math.max(rect.width, 160)
    const left = Math.min(
      Math.max(viewportPadding, rect.left),
      Math.max(viewportPadding, window.innerWidth - width - viewportPadding)
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
  }, [menuMaxHeight])

  useLayoutEffect(() => {
    if (!open) return
    updatePosition()
  }, [open, updatePosition])

  useLayoutEffect(() => {
    if (!open || !position) return
    updatePosition()
  }, [open, options, position, updatePosition])

  useEffect(() => {
    if (!open) return

    function handlePointerDown(event: MouseEvent): void {
      const target = event.target as Node
      if (triggerRef.current?.contains(target) || menuRef.current?.contains(target)) return
      setOpen(false)
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
    if (disabled) return
    setActiveIndex(selectedEnabledIndex)
    setOpen(true)
  }

  function selectOption(index: number): void {
    const option = options[index]
    if (!option || option.disabled) return

    const completeSelection = (): void => {
      setOpen(false)
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
        setOpen(false)
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
              <span className="dc-settings-select-option__copy">
                <span className="dc-settings-select-option__label">{option.label}</span>
                {option.description && (
                  <span className="dc-settings-select-option__description">
                    {option.description}
                  </span>
                )}
              </span>
              <span className="dc-settings-select-option__check" aria-hidden="true">
                {selected && <Check size={15} strokeWidth={1.8} />}
              </span>
            </div>
          )
        })}
      </div>,
      document.body
    )
  }, [activeIndex, applyValueChange, ariaLabel, listboxId, open, options, position, value])

  return (
    <>
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
            setOpen(false)
          } else {
            openMenu()
          }
        }}
        onKeyDown={handleKeyDown}
        style={style}
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
      {menu}
    </>
  )
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
