import {
  useEffect,
  useId,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
  type JSX,
  type MouseEvent as ReactMouseEvent
} from 'react'
import { createPortal } from 'react-dom'
import { Check, ChevronDown, ChevronLeft, ChevronRight, RotateCcw, Zap } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useMenuAim } from '../../hooks/useMenuAim'
import type { InferenceSpeedWire, ModelCatalogItem, ReasoningEffortWire } from '../../stores/modelCatalogStore'
import type { ContextWindowMode } from '../../types/thread'
import { ActionTooltip } from '../ui/ActionTooltip'
import { IconButton } from '../ui/IconButton'
import { PillSwitch } from '../ui/PillSwitch'
import type { ShortcutSpec } from '../ui/shortcutKeys'
import {
  composerFooterControlActiveBackground,
  composerFooterControlBoxStyle,
  composerFooterControlHoverBackground
} from './ComposerShell'
import { EffortSlider } from './EffortSlider'
import { ComposerOverlapBand, useComposerOverlapBandHeight } from './useComposerOverlapBand'

export type ReasoningQuickValue = 'default' | 'off' | ReasoningEffortWire

export interface ModelPickerProps {
  providerId?: string
  providerOptions?: Array<{ id: string; displayName: string }>
  modelName: string
  modelOptions: string[]
  modelCatalog?: ModelCatalogItem[]
  reasoningValue?: ReasoningQuickValue
  speedValue?: InferenceSpeedWire
  disabled?: boolean
  loading?: boolean
  unsupported?: boolean
  errorMessage?: string | null
  modelListReady?: boolean
  onChange?: (model: string) => void
  onProviderChange?: (providerId: string) => void
  onReasoningChange?: (value: ReasoningQuickValue) => void
  onSpeedChange?: (value: InferenceSpeedWire) => void
  onRetry?: () => void
  shortcut?: ShortcutSpec
  triggerStyle: CSSProperties
  /** MAX Mode only renders when `onContextModeChange` is provided. */
  contextMode?: ContextWindowMode
  contextSupportsMax?: boolean
  contextDegraded?: boolean
  contextConfiguredWindow?: number
  onContextModeChange?: (mode: ContextWindowMode) => void
  allowDefaultModel?: boolean
  triggerVariant?: 'composer' | 'field'
  triggerId?: string
  triggerAriaLabel?: string
}

type EffectiveReasoningValue = Exclude<ReasoningQuickValue, 'default'>
type SecondaryMenu = 'provider' | 'model'
type View = 'panel' | 'menu'

const MAIN_MENU_WIDTH = 282
const MODEL_MENU_WIDTH = 310
const PROVIDER_MENU_WIDTH = 280
const MAX_SUBMENU_HEIGHT = 320
const VIEWPORT_PADDING = 8

export function ModelPicker({
  providerId,
  providerOptions = [],
  modelName,
  modelOptions,
  modelCatalog = [],
  reasoningValue = 'off',
  speedValue = 'standard',
  disabled = false,
  loading = false,
  unsupported = false,
  errorMessage = null,
  modelListReady = false,
  onChange,
  onProviderChange,
  onReasoningChange,
  onSpeedChange,
  onRetry,
  shortcut,
  triggerStyle,
  contextMode = 'default',
  contextSupportsMax = false,
  contextDegraded = false,
  contextConfiguredWindow = 0,
  onContextModeChange,
  allowDefaultModel = true,
  triggerVariant = 'composer',
  triggerId,
  triggerAriaLabel
}: ModelPickerProps): JSX.Element {
  const t = useT()
  const contextEnabled = typeof onContextModeChange === 'function'
  const contextMaxActive = contextMode === 'max' || contextDegraded
  const [open, setOpen] = useState(false)
  const [view, setView] = useState<View>('panel')
  const [triggerActive, setTriggerActive] = useState(false)
  const [secondary, setSecondary] = useState<SecondaryMenu | null>(null)
  const [secondaryTop, setSecondaryTop] = useState(6)
  const [secondaryOpensLeft, setSecondaryOpensLeft] = useState(false)
  const [popupShiftX, setPopupShiftX] = useState(0)
  const [submenuShiftY, setSubmenuShiftY] = useState(0)
  const [submenuMaxHeight, setSubmenuMaxHeight] = useState(MAX_SUBMENU_HEIGHT)
  const [mainHighlight, setMainHighlight] = useState(0)
  const [submenuHighlight, setSubmenuHighlight] = useState(0)
  const wrapRef = useRef<HTMLDivElement>(null)
  const popupRef = useRef<HTMLDivElement>(null)
  const submenuRef = useRef<HTMLDivElement>(null)
  const {
    track: trackMenuAim,
    guard: guardMenuAim,
    cancel: cancelMenuAim
  } = useMenuAim({
    submenuRef,
    side: secondaryOpensLeft ? 'left' : 'right'
  })
  const menuId = useId()
  // The popup is portaled to document.body so the conversation column's
  // `overflow: hidden` cannot clip it; fixed offsets anchor it to the trigger.
  const [anchor, setAnchor] = useState<{ right: number; bottom: number } | null>(null)
  const overlapBandHeight = useComposerOverlapBandHeight(popupRef, open, wrapRef)

  const activeModel = modelCatalog.find((model) => model.id === modelName)
  const capability = activeModel?.reasoning ?? null
  const speedCapability = activeModel?.speed ?? null
  const speedVisible = speedCapability?.supportedModes.includes('fast') === true
  const providerVisible = Boolean(providerId && providerOptions.length > 0 && onProviderChange)
  const providerOffset = providerVisible ? 1 : 0
  const effectiveReasoning: EffectiveReasoningValue = reasoningValue === 'default'
    ? capability?.defaultEffort ?? 'medium'
    : reasoningValue

  const modelChoices = useMemo(() => {
    const withDefault = allowDefaultModel
      ? ['Default', ...modelOptions.filter((option) => option !== 'Default')]
      : modelOptions.filter((option) => option !== 'Default')
    if (!modelName || modelName === 'Default' || withDefault.includes(modelName)) return withDefault
    if (modelListReady && modelOptions.length > 0) return withDefault
    return [modelName, ...withDefault]
  }, [allowDefaultModel, modelListReady, modelName, modelOptions])

  const effortStops = useMemo(() => {
    const next: EffectiveReasoningValue[] = []
    if (capability?.supportsDisable) next.push('off')
    for (const option of capability?.supportedEfforts ?? []) next.push(option.effort)
    if (!next.includes(effectiveReasoning)) next.unshift(effectiveReasoning)
    return Array.from(new Set(next)).map((value) => ({ value, label: reasoningValueLabel(t, value) }))
  }, [capability, effectiveReasoning, t])

  const hasError = Boolean(errorMessage)
  const interactive = !disabled && !loading && (!unsupported || hasError)
  const tooltipLabel = t('composer.selectModelTitle')
  const disabledReason = loading
    ? t('composer.modelListLoading')
    : unsupported && !hasError
      ? t('composer.modelListUnsupportedTitle')
      : undefined

  const defaultSpeed = speedCapability?.defaultMode ?? 'standard'
  const effortDiffers = capability !== null && effectiveReasoning !== capability.defaultEffort
  const speedDiffers = speedVisible && speedValue !== defaultSpeed
  const contextDiffers = contextEnabled && contextMaxActive
  const differsFromDefaults = effortDiffers || speedDiffers || contextDiffers

  useLayoutEffect(() => {
    if (!open) {
      setAnchor(null)
      return
    }
    const compute = (): void => {
      const rect = wrapRef.current?.getBoundingClientRect()
      if (!rect) return
      // Right-align the popup to the trigger and open upward (8px gap), anchored
      // from the viewport edges so `position: fixed` places it correctly.
      setAnchor({
        right: Math.max(VIEWPORT_PADDING, window.innerWidth - rect.right),
        bottom: Math.max(VIEWPORT_PADDING, window.innerHeight - rect.top + 8)
      })
    }
    compute()
    window.addEventListener('resize', compute)
    window.addEventListener('scroll', compute, true)
    return () => {
      window.removeEventListener('resize', compute)
      window.removeEventListener('scroll', compute, true)
    }
  }, [open])

  const showPanel = (): void => {
    cancelMenuAim()
    setSecondary(null)
    setPopupShiftX(0)
    setView('panel')
    requestAnimationFrame(() => popupRef.current?.querySelector<HTMLElement>('[data-panel-model]')?.focus())
  }

  const showMenu = (): void => {
    setMainHighlight(providerOffset + 1)
    setView('menu')
    requestAnimationFrame(() => {
      const rows = popupRef.current?.querySelectorAll<HTMLButtonElement>('[data-main-action]')
      rows?.[providerOffset + 1]?.focus()
    })
  }

  useEffect(() => {
    if (!open) {
      setSecondary(null)
      setView('panel')
      setPopupShiftX(0)
      setSubmenuShiftY(0)
      setSubmenuMaxHeight(MAX_SUBMENU_HEIGHT)
      cancelMenuAim()
      return
    }

    const mainButtons = (): HTMLButtonElement[] =>
      Array.from(popupRef.current?.querySelectorAll<HTMLButtonElement>('[data-main-action]') ?? [])
        .filter((button) => !button.disabled)

    const submenuButtons = (): HTMLButtonElement[] =>
      Array.from(submenuRef.current?.querySelectorAll<HTMLButtonElement>('[data-submenu-option]') ?? [])
        .filter((button) => !button.disabled)

    const focusButton = (buttons: HTMLButtonElement[], index: number): void => {
      if (buttons.length === 0) return
      buttons[Math.max(0, Math.min(buttons.length - 1, index))]?.focus()
    }

    const handlePointerDown = (event: MouseEvent): void => {
      const target = event.target as Node
      // The portaled popup is not inside wrapRef, so both refs count as "inside".
      if (wrapRef.current?.contains(target) || popupRef.current?.contains(target)) return
      cancelMenuAim()
      setOpen(false)
    }

    const handleKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') {
        event.preventDefault()
        if (secondary) {
          cancelMenuAim()
          setSecondary(null)
          setPopupShiftX(0)
          requestAnimationFrame(() => focusButton(mainButtons(), mainHighlight))
        } else if (view === 'menu') {
          showPanel()
        } else {
          cancelMenuAim()
          setOpen(false)
        }
        return
      }

      if (view === 'panel') {
        // The scale and the panel's buttons answer their own keys; from the trigger an arrow steps in.
        if (!event.key.startsWith('Arrow') || popupRef.current?.contains(document.activeElement)) return
        event.preventDefault()
        const popup = popupRef.current
        ;(popup?.querySelector<HTMLElement>('input[type="range"]') ?? popup?.querySelector<HTMLElement>('[data-panel-model]'))?.focus()
        return
      }

      if (secondary) {
        const buttons = submenuButtons()
        const current = Math.max(0, buttons.indexOf(document.activeElement as HTMLButtonElement))
        if (event.key === 'ArrowDown') {
          event.preventDefault()
          const next = Math.min(buttons.length - 1, current + 1)
          setSubmenuHighlight(next)
          focusButton(buttons, next)
        } else if (event.key === 'ArrowUp') {
          event.preventDefault()
          const next = Math.max(0, current - 1)
          setSubmenuHighlight(next)
          focusButton(buttons, next)
        } else if (event.key === 'Home') {
          event.preventDefault()
          setSubmenuHighlight(0)
          focusButton(buttons, 0)
        } else if (event.key === 'End') {
          event.preventDefault()
          setSubmenuHighlight(buttons.length - 1)
          focusButton(buttons, buttons.length - 1)
        } else if (event.key === 'ArrowLeft') {
          event.preventDefault()
          cancelMenuAim()
          setSecondary(null)
          setPopupShiftX(0)
          requestAnimationFrame(() => focusButton(mainButtons(), mainHighlight))
        }
        return
      }

      const buttons = mainButtons()
      const current = Math.max(0, buttons.indexOf(document.activeElement as HTMLButtonElement))
      if (event.key === 'ArrowDown') {
        event.preventDefault()
        const next = Math.min(buttons.length - 1, current + 1)
        setMainHighlight(next)
        focusButton(buttons, next)
      } else if (event.key === 'ArrowUp') {
        event.preventDefault()
        const next = Math.max(0, current - 1)
        setMainHighlight(next)
        focusButton(buttons, next)
      } else if (event.key === 'Home') {
        event.preventDefault()
        setMainHighlight(0)
        focusButton(buttons, 0)
      } else if (event.key === 'End') {
        event.preventDefault()
        setMainHighlight(buttons.length - 1)
        focusButton(buttons, buttons.length - 1)
      } else if (event.key === 'ArrowRight') {
        const active = buttons[current]
        if (active?.dataset.submenu) {
          event.preventDefault()
          active.click()
          requestAnimationFrame(() => focusButton(submenuButtons(), 0))
        }
      } else if (event.key === 'ArrowLeft') {
        event.preventDefault()
        showPanel()
      }
    }

    document.addEventListener('mousedown', handlePointerDown, true)
    document.addEventListener('keydown', handleKeyDown, true)
    return () => {
      document.removeEventListener('mousedown', handlePointerDown, true)
      document.removeEventListener('keydown', handleKeyDown, true)
    }
  })

  useLayoutEffect(() => {
    const popup = popupRef.current
    const submenu = submenuRef.current
    if (!open || !secondary || !popup || !submenu) {
      setSubmenuShiftY(0)
      setSubmenuMaxHeight(MAX_SUBMENU_HEIGHT)
      return
    }

    const measure = (): void => {
      const popupRect = popup.getBoundingClientRect()
      const submenuRect = submenu.getBoundingClientRect()
      const viewportHeight = window.innerHeight || document.documentElement.clientHeight
      const viewportAvailableHeight = Math.max(1, Math.floor(viewportHeight - VIEWPORT_PADDING * 2))
      const nextMaxHeight = Math.min(MAX_SUBMENU_HEIGHT, viewportAvailableHeight)
      const naturalHeight = Math.max(submenu.scrollHeight, submenu.offsetHeight, submenuRect.height)
      const renderedHeight = Math.min(naturalHeight || nextMaxHeight, nextMaxHeight)
      const desiredViewportTop = popupRect.top + secondaryTop
      // Preserve row alignment when it fits. The composer card is intentionally not
      // a collision boundary; first shift within the viewport, then scroll only when
      // the submenu itself is taller than the available viewport.
      const nextViewportTop = Math.max(
        VIEWPORT_PADDING,
        Math.min(desiredViewportTop, viewportHeight - VIEWPORT_PADDING - renderedHeight)
      )
      const nextShiftY = Math.round(nextViewportTop - desiredViewportTop)

      setSubmenuShiftY((current) => (current === nextShiftY ? current : nextShiftY))
      setSubmenuMaxHeight((current) => (current === nextMaxHeight ? current : nextMaxHeight))
    }

    measure()
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(measure)
    observer?.observe(popup)
    observer?.observe(submenu)
    window.addEventListener('resize', measure)
    window.addEventListener('scroll', measure, true)
    return () => {
      observer?.disconnect()
      window.removeEventListener('resize', measure)
      window.removeEventListener('scroll', measure, true)
    }
  }, [open, secondary, secondaryTop])

  useEffect(() => {
    if (!shortcut) return
    const handleShortcut = (event: KeyboardEvent): void => {
      const mod = event.ctrlKey || event.metaKey
      if (
        !mod ||
        !event.shiftKey ||
        event.altKey ||
        event.isComposing ||
        event.key.toLowerCase() !== 'm' ||
        !interactive
      ) {
        return
      }
      event.preventDefault()
      event.stopPropagation()
      cancelMenuAim()
      setMainHighlight(0)
      setSecondary(null)
      setView('panel')
      setOpen(true)
    }

    window.addEventListener('keydown', handleShortcut, true)
    return () => window.removeEventListener('keydown', handleShortcut, true)
  }, [cancelMenuAim, interactive, shortcut])

  const openSecondary = (kind: SecondaryMenu, row: HTMLButtonElement): void => {
    const popupRect = popupRef.current?.getBoundingClientRect()
    const secondaryWidth = kind === 'provider' ? PROVIDER_MENU_WIDTH : MODEL_MENU_WIDTH
    let shouldOpenLeft = false
    let nextShiftX = 0
    if (popupRect) {
      const baseLeft = popupRect.left - popupShiftX
      const baseRight = popupRect.right - popupShiftX
      const leftSpace = baseLeft - 12
      const rightSpace = window.innerWidth - 12 - baseRight
      if (rightSpace >= secondaryWidth - 1) {
        shouldOpenLeft = false
      } else if (leftSpace >= secondaryWidth - 1) {
        shouldOpenLeft = true
      } else if (leftSpace >= rightSpace) {
        shouldOpenLeft = true
        nextShiftX = 12 - (baseLeft - secondaryWidth + 1)
      } else {
        shouldOpenLeft = false
        nextShiftX = window.innerWidth - 12 - (baseRight + secondaryWidth - 1)
      }
    }
    setSecondary(kind)
    setSecondaryTop(Math.max(6, row.offsetTop - 6))
    setSecondaryOpensLeft(shouldOpenLeft)
    setPopupShiftX(nextShiftX)
    setSubmenuHighlight(0)
  }

  const handleSecondaryPointer = (
    kind: SecondaryMenu,
    index: number,
    event: ReactMouseEvent<HTMLButtonElement>
  ): void => {
    if (secondary === kind) {
      trackMenuAim(event)
      setMainHighlight(index)
      return
    }

    const row = event.currentTarget
    if (secondary) {
      guardMenuAim(event, () => {
        setMainHighlight(index)
        openSecondary(kind, row)
      })
      return
    }

    cancelMenuAim()
    setMainHighlight(index)
    openSecondary(kind, row)
    trackMenuAim(event)
  }

  const handlePlainRowPointer = (index: number, event: ReactMouseEvent<HTMLElement>): void => {
    if (secondary) {
      guardMenuAim(event, () => {
        setMainHighlight(index)
        setSecondary(null)
        setPopupShiftX(0)
      })
      return
    }

    cancelMenuAim()
    setMainHighlight(index)
    setSecondary(null)
    setPopupShiftX(0)
  }

  const closePicker = (): void => {
    cancelMenuAim()
    setSecondary(null)
    setPopupShiftX(0)
    setOpen(false)
  }

  const selectModel = (nextModel: string): void => {
    onChange?.(nextModel)
    closePicker()
  }

  const resetDefaults = (): void => {
    if (effortDiffers && capability) onReasoningChange?.(capability.defaultEffort)
    if (speedDiffers) onSpeedChange?.(defaultSpeed)
    if (contextDiffers) onContextModeChange?.('default')
  }

  const modelLabel = modelName === 'Default' ? t('composer.defaultModel') : modelName
  const reasoningDisplayLabel = reasoningValueLabel(t, effectiveReasoning)
  const contextDisabled = loading || (!contextSupportsMax && !contextDegraded)
  const maxTag = contextEnabled && contextMaxActive
    ? <span className={`model-picker-max${contextDegraded ? ' is-degraded' : ''}`}>MAX</span>
    : null

  return (
    <div
      ref={wrapRef}
      data-composer-overlay-open={open ? 'true' : 'false'}
      style={{
        ...composerFooterControlBoxStyle,
        position: 'relative',
        minWidth: 0,
        width: triggerVariant === 'field' ? '100%' : undefined,
        height: triggerVariant === 'field' ? '38px' : undefined,
        display: triggerVariant === 'field' ? 'flex' : undefined
      }}
    >
      <ActionTooltip
        label={tooltipLabel}
        shortcut={shortcut}
        disabledReason={disabledReason}
        placement="top"
        wrapperStyle={{
          minWidth: 0,
          width: triggerVariant === 'field' ? '100%' : undefined,
          display: triggerVariant === 'field' ? 'flex' : undefined,
          flex: triggerVariant === 'field' ? '1 1 auto' : undefined
        }}
      >
        <button
          id={triggerId}
          type="button"
          value={modelName}
          aria-label={triggerAriaLabel ?? tooltipLabel}
          aria-haspopup={interactive ? 'dialog' : undefined}
          aria-expanded={interactive ? open : undefined}
          aria-controls={interactive && open ? menuId : undefined}
          disabled={!interactive}
          onMouseEnter={() => setTriggerActive(true)}
          onMouseLeave={() => setTriggerActive(false)}
          onFocus={(event) => {
            if (event.currentTarget.matches(':focus-visible')) setTriggerActive(true)
          }}
          onBlur={() => setTriggerActive(false)}
          onClick={() => {
            if (!interactive) return
            if (open) closePicker()
            else {
              setMainHighlight(0)
              setSecondary(null)
              setView('panel')
              setOpen(true)
            }
          }}
          style={{
            ...triggerStyle,
            backgroundColor: interactive
              ? open
                ? composerFooterControlActiveBackground
                : triggerActive
                  ? composerFooterControlHoverBackground
                  : 'transparent'
              : 'transparent',
            cursor: interactive ? 'pointer' : 'default'
          }}
        >
          {loading ? (
            <span style={ellipsisStyle}>{t('composer.modelListLoading')}</span>
          ) : (
            <span style={{ display: 'inline-flex', alignItems: 'center', gap: '7px', minWidth: 0, overflow: 'hidden', whiteSpace: 'nowrap' }}>
              {speedVisible && speedValue === 'fast' && (
                <Zap
                  aria-hidden
                  size={12}
                  strokeWidth={2.4}
                  fill="currentColor"
                  style={{
                    flexShrink: 0,
                    color: disabled ? 'var(--composer-footer-muted)' : 'var(--composer-footer-highlight)'
                  }}
                />
              )}
              <span
                style={{
                  ...ellipsisStyle,
                  color: disabled ? 'var(--composer-footer-muted)' : 'var(--composer-footer-highlight)'
                }}
              >
                {modelLabel}
              </span>
              <span
                style={{
                  flexShrink: 0,
                  color: disabled ? 'var(--composer-footer-muted)' : 'var(--composer-footer-text)',
                  fontWeight: 'var(--type-ui-weight)'
                }}
              >
                {reasoningDisplayLabel}
              </span>
              {maxTag}
            </span>
          )}
          {interactive && (
            <ChevronDown
              aria-hidden
              size={13}
              strokeWidth={1.8}
              style={{
                flexShrink: 0,
                color: 'var(--composer-footer-muted)',
                transform: open ? 'rotate(180deg)' : 'none',
                transition: 'transform 120ms ease'
              }}
            />
          )}
        </button>
      </ActionTooltip>

      {open && createPortal(
        <div
          ref={popupRef}
          id={menuId}
          role="dialog"
          aria-label={tooltipLabel}
          style={{
            position: 'fixed',
            right: `${anchor?.right ?? 0}px`,
            bottom: `${anchor?.bottom ?? 0}px`,
            // Hidden until the trigger is measured to avoid a first-frame flash
            // at the wrong position.
            visibility: anchor ? 'visible' : 'hidden',
            zIndex: 1100,
            width: `${MAIN_MENU_WIDTH}px`,
            padding: '6px',
            border: 'none',
            borderRadius: '12px',
            overflow: 'visible',
            background: 'var(--glass-surface-strong)',
            boxShadow: 'var(--glass-shadow-soft)',
            backdropFilter: 'var(--glass-blur)',
            WebkitBackdropFilter: 'var(--glass-blur)',
            transform: popupShiftX === 0 ? undefined : `translateX(${popupShiftX}px)`
          }}
        >
          <ComposerOverlapBand height={overlapBandHeight} />
          {errorMessage && (
            <div
              role="status"
              aria-live="polite"
              style={{
                display: 'flex',
                flexDirection: 'column',
                gap: '3px',
                marginBottom: '5px',
                padding: '8px 9px',
                borderRadius: '8px',
                background: 'color-mix(in srgb, var(--error) 8%, transparent)',
                color: 'var(--error)',
                fontSize: '11px',
                lineHeight: 1.35
              }}
            >
              <strong>{t('composer.modelListError')}</strong>
              <span style={{ color: 'var(--text-secondary)' }}>{errorMessage}</span>
              {onRetry && (
                <button
                  type="button"
                  onClick={(event) => {
                    event.stopPropagation()
                    onRetry()
                  }}
                  style={{
                    alignSelf: 'flex-start',
                    padding: '3px 0 0',
                    border: 'none',
                    background: 'transparent',
                    color: 'var(--accent)',
                    cursor: 'pointer',
                    fontSize: '11px',
                    fontWeight: 600
                  }}
                >
                  {t('composer.modelListRetry')}
                </button>
              )}
            </div>
          )}

          {view === 'panel' ? (
            <div className="model-picker-panel">
              <div className="model-picker-head">
                {speedVisible ? (
                  <IconButton
                    size={32}
                    className="model-picker-fast"
                    aria-pressed={speedValue === 'fast'}
                    label={t('composer.speed.fast')}
                    tooltipLabel={t('composer.speed.fast')}
                    tooltipPlacement="top"
                    onClick={() => onSpeedChange?.(speedValue === 'fast' ? 'standard' : 'fast')}
                    icon={<Zap aria-hidden size={16} fill={speedValue === 'fast' ? 'currentColor' : 'none'} />}
                  />
                ) : (
                  <span className="model-picker-slot" />
                )}
                <div className={`model-picker-title${capability ? '' : ' model-picker-title--model'}`}>
                  {(capability || maxTag) && (
                    <span className="model-picker-effort">
                      {capability && reasoningDisplayLabel}
                      {maxTag}
                    </span>
                  )}
                  <button type="button" className="model-picker-model" data-panel-model aria-haspopup="menu" onClick={showMenu}>
                    <span>{modelLabel}</span>
                    <ChevronRight aria-hidden size={14} strokeWidth={1.8} />
                  </button>
                </div>
                {differsFromDefaults ? (
                  <IconButton
                    size={32}
                    label={t('composer.reasoning.resetToDefault')}
                    tooltipLabel={t('composer.reasoning.resetToDefault')}
                    tooltipPlacement="top"
                    onClick={resetDefaults}
                    icon={<RotateCcw aria-hidden size={16} />}
                  />
                ) : (
                  <span className="model-picker-slot" />
                )}
              </div>
              {capability && (
                <EffortSlider
                  stops={effortStops}
                  value={effectiveReasoning}
                  ariaLabel={t('composer.reasoning.heading')}
                  fast={speedVisible && speedValue === 'fast'}
                  onChange={(value) => onReasoningChange?.(value)}
                />
              )}
            </div>
          ) : (
            <div role="menu" aria-label={t('composer.modelHeading')}>
              <button
                type="button"
                role="menuitem"
                data-main-action
                className="model-picker-back"
                data-highlighted={mainHighlight === 0 ? 'true' : undefined}
                onMouseEnter={(event) => handlePlainRowPointer(0, event)}
                onMouseMove={(event) => handlePlainRowPointer(0, event)}
                onClick={showPanel}
              >
                <ChevronLeft aria-hidden size={15} strokeWidth={1.7} />
                {t('composer.modelMenu.back')}
              </button>

              {providerVisible && (
                <MainMenuRow
                  label={t('composer.providerHeading')}
                  value={providerOptions.find((provider) => provider.id === providerId)?.displayName ?? providerId ?? ''}
                  highlighted={mainHighlight === 1 || secondary === 'provider'}
                  submenu="provider"
                  onHover={(event) => handleSecondaryPointer('provider', 1, event)}
                  onClick={(event) => {
                    cancelMenuAim()
                    setMainHighlight(1)
                    openSecondary('provider', event.currentTarget)
                  }}
                />
              )}

              <MainMenuRow
                label={t('composer.modelHeading')}
                value={modelLabel}
                highlighted={mainHighlight === providerOffset + 1 || secondary === 'model'}
                submenu="model"
                onHover={(event) => handleSecondaryPointer('model', providerOffset + 1, event)}
                onClick={(event) => {
                  cancelMenuAim()
                  setMainHighlight(providerOffset + 1)
                  openSecondary('model', event.currentTarget)
                }}
              />
              {contextEnabled && (
                <>
                  <div
                    style={mainMenuRowStyle(mainHighlight === providerOffset + 2, contextDisabled, true)}
                    onMouseEnter={(event) => handlePlainRowPointer(providerOffset + 2, event)}
                    onMouseMove={(event) => handlePlainRowPointer(providerOffset + 2, event)}
                  >
                    <span style={mainLabelStyle}>{t('composer.context.label')}</span>
                    <span style={{ ...trailingSlotStyle, transform: 'translateX(-4px)' }}>
                      <PillSwitch
                        checked={contextMaxActive}
                        onChange={(checked) => onContextModeChange(checked ? 'max' : 'default')}
                        size="sm"
                        disabled={contextDisabled}
                        aria-label={t('composer.context.label')}
                      />
                    </span>
                  </div>
                  {contextDegraded && (
                    <div
                      style={{
                        margin: '-1px 9px 5px',
                        color: 'var(--permission-full-access)',
                        fontSize: '10px',
                        lineHeight: 1.35
                      }}
                    >
                      {t('composer.context.degraded', { window: formatContextWindow(contextConfiguredWindow) })}
                    </div>
                  )}
                </>
              )}

              {secondary && (
                <div
                  ref={submenuRef}
                  role="listbox"
                  aria-label={secondary === 'provider' ? t('composer.providerHeading') : t('composer.modelHeading')}
                  style={submenuStyle(secondary, secondaryTop + submenuShiftY, secondaryOpensLeft, submenuMaxHeight)}
                  onMouseEnter={cancelMenuAim}
                  onMouseMove={cancelMenuAim}
                >
                  {secondary === 'provider'
                    ? providerOptions.map((provider, index) => (
                        <OptionRow
                          key={provider.id}
                          selected={provider.id === providerId}
                          highlighted={submenuHighlight === index}
                          label={provider.displayName}
                          description={provider.id === provider.displayName ? undefined : provider.id}
                          onHover={() => setSubmenuHighlight(index)}
                          onSelect={() => {
                            onProviderChange?.(provider.id)
                            closePicker()
                          }}
                        />
                      ))
                    : modelChoices.map((model, index) => (
                        <OptionRow
                          key={model}
                          selected={model === modelName || (model === 'Default' && modelName === 'Default')}
                          highlighted={submenuHighlight === index}
                          label={model === 'Default' ? t('composer.defaultModel') : model}
                          onHover={() => setSubmenuHighlight(index)}
                          onSelect={() => selectModel(model)}
                        />
                      ))}
                </div>
              )}
            </div>
          )}
        </div>,
        document.body
      )}
    </div>
  )
}

function MainMenuRow({
  label,
  value,
  highlighted,
  submenu,
  onHover,
  onClick
}: {
  label: string
  value: string
  highlighted: boolean
  submenu: SecondaryMenu
  onHover: (event: ReactMouseEvent<HTMLButtonElement>) => void
  onClick: (event: ReactMouseEvent<HTMLButtonElement>) => void
}): JSX.Element {
  return (
    <button
      type="button"
      role="menuitem"
      aria-haspopup="listbox"
      data-main-action
      data-submenu={submenu}
      onMouseEnter={onHover}
      onMouseMove={onHover}
      onClick={onClick}
      style={mainMenuRowStyle(highlighted)}
    >
      <span style={mainLabelStyle}>{label}</span>
      <span style={mainValueStyle}>{value}</span>
      <span style={trailingSlotStyle} aria-hidden>
        <ChevronRight size={15} strokeWidth={1.7} />
      </span>
    </button>
  )
}

function OptionRow({
  selected,
  highlighted,
  label,
  description,
  onHover,
  onSelect
}: {
  selected: boolean
  highlighted: boolean
  label: string
  description?: string
  onHover?: () => void
  onSelect?: () => void
}): JSX.Element {
  return (
    <button
      type="button"
      role="option"
      aria-selected={selected}
      data-submenu-option
      onMouseEnter={onHover}
      onFocus={onHover}
      onClick={onSelect}
      style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: '10px',
        width: '100%',
        minHeight: '35px',
        padding: '7px 9px',
        border: 'none',
        borderRadius: '7px',
        background: highlighted ? 'var(--bg-tertiary)' : 'transparent',
        color: highlighted || selected ? 'var(--text-primary)' : 'var(--text-secondary)',
        cursor: 'pointer',
        textAlign: 'left'
      }}
    >
      <span style={{ display: 'flex', minWidth: 0, flex: 1, flexDirection: 'column', gap: '2px' }}>
        <span style={{ ...ellipsisStyle, fontSize: '12px' }}>{label}</span>
        {description && (
          <small style={{ color: 'var(--text-dimmed)', fontSize: '10px', lineHeight: 1.3 }}>
            {description}
          </small>
        )}
      </span>
      <Check
        aria-hidden
        size={15}
        strokeWidth={2}
        style={{ flexShrink: 0, color: 'var(--text-primary)', opacity: selected ? 1 : 0 }}
      />
    </button>
  )
}

const ellipsisStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const mainLabelStyle: CSSProperties = {
  ...ellipsisStyle,
  fontSize: '13px'
}

const mainValueStyle: CSSProperties = {
  ...ellipsisStyle,
  color: 'var(--text-secondary)',
  textAlign: 'right',
  fontSize: '12px'
}

const trailingSlotStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  justifySelf: 'end',
  width: '32px',
  height: '100%'
}

function mainMenuRowStyle(
  highlighted: boolean,
  disabled = false,
  contextRow = false
): CSSProperties {
  return {
    display: 'grid',
    gridTemplateColumns: contextRow ? 'minmax(0, 1fr) 32px' : 'minmax(82px, 1fr) minmax(0, 110px) 32px',
    alignItems: 'center',
    gap: 0,
    width: '100%',
    minHeight: '40px',
    padding: '0 4px 0 9px',
    border: 'none',
    borderRadius: '8px',
    background: highlighted ? 'var(--bg-tertiary)' : 'transparent',
    color: 'var(--text-primary)',
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.48 : 1,
    textAlign: 'left'
  }
}

function submenuStyle(kind: SecondaryMenu, top: number, opensLeft: boolean, maxHeight: number): CSSProperties {
  const width = kind === 'provider' ? PROVIDER_MENU_WIDTH : MODEL_MENU_WIDTH
  return {
    position: 'absolute',
    top,
    left: opensLeft ? `calc(-${width}px + 1px)` : 'calc(100% - 1px)',
    zIndex: 72,
    width,
    boxSizing: 'border-box',
    maxHeight: `${maxHeight}px`,
    padding: '6px',
    overflowX: 'hidden',
    overflowY: 'auto',
    borderTop: 'none',
    borderRight: opensLeft ? '1px solid var(--glass-border)' : 'none',
    borderBottom: 'none',
    borderLeft: opensLeft ? 'none' : '1px solid var(--glass-border)',
    borderRadius: '10px',
    background: 'var(--glass-surface-strong)',
    boxShadow: 'var(--glass-shadow-soft)',
    backdropFilter: 'var(--glass-blur)',
    WebkitBackdropFilter: 'var(--glass-blur)'
  }
}

function reasoningValueLabel(t: ReturnType<typeof useT>, value: EffectiveReasoningValue): string {
  switch (value) {
    case 'off': return t('composer.reasoning.off')
    case 'low': return t('composer.reasoning.low')
    case 'medium': return t('composer.reasoning.medium')
    case 'high': return t('composer.reasoning.high')
    case 'extraHigh': return t('composer.reasoning.extraHigh')
    case 'ultra': return t('composer.reasoning.ultra')
  }
}

function formatContextWindow(n: number): string {
  if (!Number.isFinite(n) || n <= 0) return ''
  if (n >= 1_000_000) {
    const value = n / 1_000_000
    return `${value % 1 === 0 ? value.toFixed(0) : value.toFixed(1)}M`
  }
  return `${Math.round(n / 1000)}K`
}
