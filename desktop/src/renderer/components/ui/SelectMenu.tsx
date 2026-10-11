import { Check } from 'lucide-react'
import {
  useCallback,
  useEffect,
  useId,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
  type JSX,
  type KeyboardEvent,
  type ReactNode
} from 'react'
import { createPortal } from 'react-dom'
import { LayerBoundary } from '../../contexts/LayerContext'
import { ActionTooltip } from './ActionTooltip'
import { useDismissOnViewportChange, type PopupAnchor } from './anchoredPopup'

export interface SelectMenuItem {
  label: ReactNode
  description?: ReactNode
  icon?: ReactNode
  tooltip?: string
  disabled?: boolean
}

export function SelectMenuOption({ id, value, option, selected, active, showTooltip, onActivate, onSelect }: {
  id: string
  value: string
  option: SelectMenuItem
  selected: boolean
  active: boolean
  showTooltip: boolean
  onActivate: () => void
  onSelect: () => void
}): JSX.Element {
  const copy = (
    <span className="dc-settings-select-option__copy">
      <span className="dc-settings-select-option__label">{option.label}</span>
      {option.description && <span className="dc-settings-select-option__description">{option.description}</span>}
    </span>
  )
  return (
    <div
      id={id}
      role="option"
      aria-selected={selected}
      aria-disabled={option.disabled || undefined}
      data-value={value}
      data-active={active || undefined}
      data-disabled={option.disabled || undefined}
      className="dc-settings-select-option"
      onMouseDown={(event) => event.preventDefault()}
      onMouseEnter={() => { if (!option.disabled) onActivate() }}
      onClick={onSelect}
    >
      {option.icon && <span className="dc-settings-select-option__icon" aria-hidden="true">{option.icon}</span>}
      {option.tooltip && showTooltip
        ? (
            <ActionTooltip label={option.tooltip} placement="right" multiline wrapperStyle={{ flex: '1 1 auto', minWidth: 0, width: '100%' }}>
              {copy}
            </ActionTooltip>
          )
        : copy}
      <span className="dc-settings-select-option__check" aria-hidden="true">
        {selected && <Check size={15} strokeWidth={1.8} />}
      </span>
    </div>
  )
}

export function firstEnabledIndex(options: ReadonlyArray<{ disabled?: boolean }>): number {
  return options.findIndex((option) => !option.disabled)
}

export function lastEnabledIndex(options: ReadonlyArray<{ disabled?: boolean }>): number {
  for (let index = options.length - 1; index >= 0; index -= 1) {
    if (!options[index].disabled) return index
  }
  return -1
}

export function nextEnabledIndex(options: ReadonlyArray<{ disabled?: boolean }>, currentIndex: number, delta: 1 | -1): number {
  const fallback = delta === 1 ? firstEnabledIndex(options) : lastEnabledIndex(options)
  if (fallback < 0) return -1
  let index = currentIndex
  for (let visited = 0; visited < options.length; visited += 1) {
    index = (index + delta + options.length) % options.length
    if (!options[index].disabled) return index
  }
  return fallback
}

export interface SelectMenuRequestOptions {
  anchor: PopupAnchor
  ariaLabel?: string
  options: ReadonlyArray<{ label: string; disabled?: boolean; group?: string }>
  selectedIndex: number
}

export type SelectMenuResult = { kind: 'select'; index: number } | { kind: 'cancel' }

interface ActiveRequest {
  id: number
  options: SelectMenuRequestOptions
  returnFocus: HTMLElement | null
  resolve(result: SelectMenuResult): void
}

const VIEWPORT_INSET = 8
const MENU_GAP = 6
const MENU_MIN_WIDTH = 160
const MENU_MAX_HEIGHT = 280

let activeRequest: ActiveRequest | null = null
let requestListener: ((request: ActiveRequest | null) => void) | null = null
let nextRequestId = 0

export function requestSelectMenu(options: SelectMenuRequestOptions): { result: Promise<SelectMenuResult>; dismiss(): void } {
  if (activeRequest || options.options.length === 0) return { result: Promise.resolve({ kind: 'cancel' }), dismiss() {} }
  let resolve!: (result: SelectMenuResult) => void
  const result = new Promise<SelectMenuResult>((settle) => { resolve = settle })
  const request: ActiveRequest = {
    id: ++nextRequestId,
    options,
    returnFocus: document.activeElement instanceof HTMLElement ? document.activeElement : null,
    resolve
  }
  activeRequest = request
  requestListener?.(request)
  return { result, dismiss: () => finishRequest(request, { kind: 'cancel' }) }
}

export function SelectMenuHost(): JSX.Element | null {
  const [request, setRequest] = useState<ActiveRequest | null>(activeRequest)

  useEffect(() => {
    requestListener = setRequest
    setRequest(activeRequest)
    return () => {
      if (requestListener === setRequest) requestListener = null
      if (activeRequest) finishRequest(activeRequest, { kind: 'cancel' })
    }
  }, [])

  if (!request) return null
  return <AnchoredSelectMenu key={request.id} options={request.options} onFinish={(result) => finishRequest(request, result)} />
}

function finishRequest(request: ActiveRequest, result: SelectMenuResult): void {
  if (activeRequest?.id !== request.id) return
  activeRequest = null
  requestListener?.(null)
  request.returnFocus?.focus()
  request.resolve(result)
}

function AnchoredSelectMenu({ options: { anchor, ariaLabel, options, selectedIndex }, onFinish }: {
  options: SelectMenuRequestOptions
  onFinish(result: SelectMenuResult): void
}): JSX.Element {
  const listboxId = useId()
  const menuRef = useRef<HTMLDivElement>(null)
  const [position, setPosition] = useState<CSSProperties | null>(null)
  const [activeIndex, setActiveIndex] = useState(() =>
    options[selectedIndex] && !options[selectedIndex].disabled ? selectedIndex : firstEnabledIndex(options))

  useLayoutEffect(() => {
    const menu = menuRef.current
    if (!menu) return
    const availableBelow = Math.max(120, window.innerHeight - anchor.y - anchor.height - VIEWPORT_INSET)
    const availableAbove = Math.max(120, anchor.y - VIEWPORT_INSET)
    const opensUp = availableBelow < 180 && availableAbove > availableBelow
    const maxHeight = Math.min(MENU_MAX_HEIGHT, opensUp ? availableAbove : availableBelow)
    const height = Math.min(maxHeight, menu.scrollHeight || maxHeight)
    const width = Math.max(anchor.width, MENU_MIN_WIDTH)
    setPosition({
      top: opensUp
        ? Math.max(VIEWPORT_INSET, anchor.y - height - MENU_GAP)
        : Math.min(window.innerHeight - VIEWPORT_INSET, anchor.y + anchor.height + MENU_GAP),
      left: Math.min(Math.max(VIEWPORT_INSET, anchor.x), Math.max(VIEWPORT_INSET, window.innerWidth - width - VIEWPORT_INSET)),
      width,
      maxHeight
    })
  }, [anchor])

  const positioned = position !== null
  useEffect(() => {
    if (positioned) menuRef.current?.focus()
  }, [positioned])

  useLayoutEffect(() => {
    if (!position || activeIndex < 0) return
    document.getElementById(`${listboxId}-option-${activeIndex}`)?.scrollIntoView?.({ block: 'nearest' })
  }, [activeIndex, listboxId, position])

  const cancel = useCallback((): void => onFinish({ kind: 'cancel' }), [onFinish])
  useDismissOnViewportChange(menuRef, cancel)
  useEffect(() => {
    const onPointerDown = (event: MouseEvent): void => {
      if (!menuRef.current?.contains(event.target as Node)) cancel()
    }
    document.addEventListener('mousedown', onPointerDown, true)
    window.addEventListener('blur', cancel)
    return () => {
      document.removeEventListener('mousedown', onPointerDown, true)
      window.removeEventListener('blur', cancel)
    }
  }, [cancel])

  const select = (index: number): void => {
    if (options[index] && !options[index].disabled) onFinish({ kind: 'select', index })
  }

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>): void => {
    const move = (next: number): void => {
      event.preventDefault()
      if (next >= 0) setActiveIndex(next)
    }
    switch (event.key) {
      case 'ArrowDown': return move(nextEnabledIndex(options, activeIndex, 1))
      case 'ArrowUp': return move(nextEnabledIndex(options, activeIndex, -1))
      case 'Home': return move(firstEnabledIndex(options))
      case 'End': return move(lastEnabledIndex(options))
      case 'Enter':
      case ' ':
        event.preventDefault()
        select(activeIndex)
        return
      case 'Escape':
      case 'Tab':
        event.preventDefault()
        event.stopPropagation()
        cancel()
    }
  }

  const menu = (
    <div
      ref={menuRef}
      id={listboxId}
      role="listbox"
      tabIndex={-1}
      aria-label={ariaLabel}
      aria-activedescendant={activeIndex >= 0 ? `${listboxId}-option-${activeIndex}` : undefined}
      className="dc-settings-select-menu"
      style={position ?? { visibility: 'hidden', top: anchor.y + anchor.height + MENU_GAP, left: anchor.x, maxHeight: MENU_MAX_HEIGHT }}
      onKeyDown={onKeyDown}
    >
      {options.map((option, index) => (
        <div key={index} role="presentation">
          {option.group && option.group !== options[index - 1]?.group && (
            <div role="presentation" className="dc-settings-select-group">{option.group}</div>
          )}
          <SelectMenuOption
            id={`${listboxId}-option-${index}`}
            value={String(index)}
            option={option}
            selected={index === selectedIndex}
            active={index === activeIndex}
            showTooltip={false}
            onActivate={() => setActiveIndex(index)}
            onSelect={() => select(index)}
          />
        </div>
      ))}
    </div>
  )
  return createPortal(<LayerBoundary>{menu}</LayerBoundary>, document.body) as JSX.Element
}
