import { ChevronDown } from 'lucide-react'
import { Input } from './Input'
import {
  useCallback,
  useEffect,
  useId,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type JSX,
  type KeyboardEvent
} from 'react'
import { createPortal } from 'react-dom'

export interface ComboboxOption {
  value: string
  label: string
}

export interface ComboboxProps {
  id?: string
  value: string
  options: ReadonlyArray<ComboboxOption>
  onValueChange: (value: string) => void
  ariaLabel?: string
  placeholder?: string
  disabled?: boolean
  style?: CSSProperties
  menuMaxHeight?: number
}

interface MenuPosition {
  top: number
  left: number
  width: number
  maxHeight: number
}

/** Editable text field with themed suggestions; values are not restricted to the option list. */
export function Combobox({
  id,
  value,
  options,
  onValueChange,
  ariaLabel,
  placeholder,
  disabled = false,
  style,
  menuMaxHeight = 240
}: ComboboxProps): JSX.Element {
  const generatedId = useId()
  const listboxId = `${id ?? generatedId}-listbox`
  const rootRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [activeIndex, setActiveIndex] = useState(0)
  const [position, setPosition] = useState<MenuPosition | null>(null)
  const filteredOptions = useMemo(() => {
    const query = value.trim().toLocaleLowerCase()
    if (!query) return options
    return options.filter((option) =>
      option.label.toLocaleLowerCase().includes(query) ||
      option.value.toLocaleLowerCase().includes(query)
    )
  }, [options, value])

  const updatePosition = useCallback(() => {
    const root = rootRef.current
    if (!root) return
    const rect = root.getBoundingClientRect()
    const viewportPadding = 8
    const menuGap = 6
    const availableBelow = Math.max(120, window.innerHeight - rect.bottom - viewportPadding)
    const availableAbove = Math.max(120, rect.top - viewportPadding)
    const opensUp = availableBelow < 180 && availableAbove > availableBelow
    const maxHeight = Math.min(menuMaxHeight, opensUp ? availableAbove : availableBelow)
    const measuredHeight = menuRef.current
      ? Math.min(maxHeight, menuRef.current.scrollHeight || menuRef.current.getBoundingClientRect().height || maxHeight)
      : maxHeight
    const width = Math.max(rect.width, 160)
    setPosition({
      top: opensUp
        ? Math.max(viewportPadding, rect.top - measuredHeight - menuGap)
        : Math.min(window.innerHeight - viewportPadding, rect.bottom + menuGap),
      left: Math.min(
        Math.max(viewportPadding, rect.left),
        Math.max(viewportPadding, window.innerWidth - width - viewportPadding)
      ),
      width,
      maxHeight
    })
  }, [menuMaxHeight])

  useLayoutEffect(() => {
    if (!open) return
    updatePosition()
  }, [open, filteredOptions, updatePosition])

  useEffect(() => {
    if (!open) return
    function handlePointerDown(event: MouseEvent): void {
      const target = event.target as Node
      if (rootRef.current?.contains(target) || menuRef.current?.contains(target)) return
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
    setActiveIndex((current) => Math.min(current, Math.max(0, filteredOptions.length - 1)))
  }, [filteredOptions.length])

  function selectOption(index: number): void {
    const option = filteredOptions[index]
    if (!option) return
    onValueChange(option.value)
    setOpen(false)
    requestAnimationFrame(() => inputRef.current?.focus())
  }

  function handleKeyDown(event: KeyboardEvent<HTMLInputElement>): void {
    if (event.key === 'ArrowDown') {
      event.preventDefault()
      if (!open) setOpen(true)
      else setActiveIndex((current) => Math.min(current + 1, filteredOptions.length - 1))
    } else if (event.key === 'ArrowUp') {
      event.preventDefault()
      if (!open) setOpen(true)
      else setActiveIndex((current) => Math.max(current - 1, 0))
    } else if (event.key === 'Enter' && open && filteredOptions.length > 0) {
      event.preventDefault()
      selectOption(activeIndex)
    } else if (event.key === 'Escape' && open) {
      event.preventDefault()
      event.stopPropagation()
      setOpen(false)
    }
  }

  const menu = open && position && filteredOptions.length > 0
    ? createPortal(
        <div
          ref={menuRef}
          id={listboxId}
          role="listbox"
          aria-label={ariaLabel}
          className="dc-combobox-menu"
          style={{ top: position.top, left: position.left, width: position.width, maxHeight: position.maxHeight }}
        >
          {filteredOptions.map((option, index) => (
            <div
              key={option.value}
              id={`${listboxId}-option-${index}`}
              role="option"
              aria-selected={option.value === value}
              data-active={index === activeIndex || undefined}
              className="dc-combobox-option"
              onMouseDown={(event) => event.preventDefault()}
              onMouseEnter={() => setActiveIndex(index)}
              onClick={() => selectOption(index)}
            >
              {option.label}
            </div>
          ))}
        </div>,
        document.body
      )
    : null

  return (
    <>
      <div
        ref={rootRef}
        className="dc-combobox"
        data-open={open || undefined}
        data-disabled={disabled || undefined}
        style={style}
        onMouseDown={(event) => {
          if (disabled || event.target === inputRef.current) return
          event.preventDefault()
          inputRef.current?.focus()
          setOpen(true)
        }}
      >
        <Input
          ref={inputRef}
          bare
          id={id}
          role="combobox"
          value={value}
          disabled={disabled}
          placeholder={placeholder}
          aria-label={ariaLabel}
          aria-autocomplete="list"
          aria-expanded={open}
          aria-controls={open ? listboxId : undefined}
          aria-activedescendant={open && filteredOptions.length > 0 ? `${listboxId}-option-${activeIndex}` : undefined}
          onFocus={() => setOpen(true)}
          onChange={(event) => {
            onValueChange(event.target.value)
            setActiveIndex(0)
            setOpen(true)
          }}
          onKeyDown={handleKeyDown}
          style={{ flex: 1, minWidth: 0, padding: '7px 4px 7px 10px' }}
        />
        <span className="dc-combobox__toggle" aria-hidden="true">
          <ChevronDown size={15} strokeWidth={1.8} />
        </span>
      </div>
      {menu}
    </>
  )
}
