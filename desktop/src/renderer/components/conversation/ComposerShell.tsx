import {
  useLayoutEffect,
  useRef,
  useState,
  type ButtonHTMLAttributes,
  type CSSProperties,
  type DragEventHandler,
  type JSX,
  type ReactNode
} from 'react'
import { Bot, ListChecks, Loader2, Square, X } from 'lucide-react'
import type {
  DesktopPluginComposerSurfaceContext,
} from '@dotcraft/plugin'
import { ActionTooltip } from '../ui/ActionTooltip'
import { DesktopPluginSurface } from '../desktopPlugins/DesktopPluginSurface'
import { ComposerMascot, ComposerMascotShadow, type MascotExpression, type MascotLight } from '@dotcraft/avatar/react'
import { MascotBubble, type MascotBubbleAction, type MascotBubbleTone } from './MascotBubble'
import { useComposerOverlayLiftHost } from './composerOverlayLift'
import { ContextMenu, type ContextMenuItem } from '../ui/ContextMenu'
import type { ShortcutSpec } from '../ui/shortcutKeys'

export interface ComposerMascotBubble {
  tone?: MascotBubbleTone
  title: string
  body?: string
  actions?: MascotBubbleAction[]
}

export interface ComposerMascotInteraction {
  expression?: MascotExpression
  light?: MascotLight
  bubble?: ComposerMascotBubble | null
  menuItems?: ContextMenuItem[]
  hold?: 'sign'
}

export type ComposerMascotReasoningEffort = 'off' | 'low' | 'medium' | 'high' | 'extraHigh'
export type ComposerMascotSpeed = 'standard' | 'fast'

export const DECISION_MASCOT: ComposerMascotInteraction = { expression: 'operator', hold: 'sign' }

type ComposerActionButtonTone = 'enabled' | 'disabled'

export const COMPOSER_FOOTER_CONTROL_HEIGHT = 24
export const composerFooterControlHoverBackground = 'var(--sidebar-control-hover)'
export const composerFooterControlActiveBackground = 'var(--sidebar-control-active)'

export const composerFooterControlBoxStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  height: COMPOSER_FOOTER_CONTROL_HEIGHT
}

interface ComposerShellProps {
  dragOver: boolean
  dropLabel: string
  topAccessory?: ReactNode
  topAccessoryVisible?: boolean
  attachmentStrip?: ReactNode
  editor: ReactNode
  footerLeading: ReactNode
  footerAction: ReactNode
  belowFooter?: ReactNode
  onDragOver: DragEventHandler<HTMLDivElement>
  onDragLeave: DragEventHandler<HTMLDivElement>
  onDrop: DragEventHandler<HTMLDivElement>
  opacity?: number
  focused?: boolean
  showMascot?: boolean
  desktopPluginSurfaceContext: DesktopPluginComposerSurfaceContext
  mascotBounceSignal?: number
  mascotInteraction?: ComposerMascotInteraction
  mascotReasoningEffort?: ComposerMascotReasoningEffort
  mascotSpeed?: ComposerMascotSpeed
  mascotContextMax?: boolean
  mascotName?: string
  mascotHandoff?: boolean
}

const COMPOSER_CARD_INLINE_PADDING = 10
const COMPOSER_CARD_BORDER = 1

interface ComposerPlanModeLabelProps {
  value: 'agent' | 'plan'
  onDisable: () => void
  label: string
  title: string
  ariaLabel: string
  shortcut?: ShortcutSpec
}

export function ComposerShell({
  dragOver,
  dropLabel,
  topAccessory,
  topAccessoryVisible = false,
  attachmentStrip,
  editor,
  footerLeading,
  footerAction,
  belowFooter,
  onDragOver,
  onDragLeave,
  onDrop,
  opacity = 1,
  focused = false,
  showMascot = false,
  desktopPluginSurfaceContext,
  mascotBounceSignal = 0,
  mascotInteraction,
  mascotReasoningEffort = 'off',
  mascotSpeed = 'standard',
  mascotContextMax = false,
  mascotName,
  mascotHandoff = false
}: ComposerShellProps): JSX.Element {
  const [hovered, setHovered] = useState(false)
  const [topAccessoryHeight, setTopAccessoryHeight] = useState(0)
  const [topAccessoryPushSignal, setTopAccessoryPushSignal] = useState(0)
  const { lift: overlayLift, api: overlayLiftApi, Provider: OverlayLiftProvider } =
    useComposerOverlayLiftHost()
  const [renderedMascotAvatar, setRenderedMascotAvatar] = useState(mascotName)
  const topAccessoryRef = useRef<HTMLDivElement | null>(null)
  const topAccessoryHeightRef = useRef(0)

  useLayoutEffect(() => {
    if (!topAccessoryVisible) {
      topAccessoryHeightRef.current = 0
      setTopAccessoryHeight(0)
      return undefined
    }
    const element = topAccessoryRef.current
    if (!element) return undefined

    let settleTimer = 0
    let grewSinceSettle = false
    const readHeight = (): number => Math.max(0, Math.round(element.getBoundingClientRect().height))
    const commitHeight = (height: number): void => {
      topAccessoryHeightRef.current = height
      setTopAccessoryHeight((current) => current === height ? current : height)
    }
    commitHeight(readHeight())
    if (typeof ResizeObserver === 'undefined') return undefined

    const observer = new ResizeObserver(() => {
      const height = readHeight()
      if (height > topAccessoryHeightRef.current) {
        // The dock is the moving floor: following expansion immediately keeps the
        // mascot on its top edge for every observed frame.
        grewSinceSettle = true
        commitHeight(height)
      }
      window.clearTimeout(settleTimer)
      settleTimer = window.setTimeout(() => {
        const settledHeight = readHeight()
        if (settledHeight !== topAccessoryHeightRef.current) commitHeight(settledHeight)
        if (grewSinceSettle) {
          grewSinceSettle = false
          setTopAccessoryPushSignal((current) => current + 1)
        }
      }, 48)
    })
    observer.observe(element)
    return () => {
      window.clearTimeout(settleTimer)
      observer.disconnect()
    }
  }, [topAccessoryVisible])


  return (
    <div
      data-composer-root
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        position: 'relative',
        padding: '0 0 14px',
        display: 'flex',
        flexDirection: 'column',
        gap: 0,
        opacity,
        isolation: 'isolate'
      }}
    >
      {showMascot && (
        <ComposerMascot
          focused={focused}
          dragOver={dragOver}
          bounceSignal={mascotBounceSignal}
          interaction={mascotInteraction ? { ...mascotInteraction, bubble: mascotInteraction.bubble ? <MascotBubble {...mascotInteraction.bubble} /> : undefined } : undefined}
          renderMenu={mascotInteraction?.menuItems?.length ? (position, close) => <ContextMenu items={mascotInteraction.menuItems!} position={position} onClose={close} /> : undefined}
          renderCharacter={(character, context) => <DesktopPluginSurface name="composer.mascot" context={{ ...desktopPluginSurfaceContext, ...context }}>{character}</DesktopPluginSurface>}
          reasoningEffort={mascotReasoningEffort}
          speed={mascotSpeed}
          contextMax={mascotContextMax}
          name={mascotName}
          onNameRendered={setRenderedMascotAvatar}
          anchorOffset={Math.max(topAccessoryHeight, overlayLift)}
          anchorPushSignal={topAccessoryPushSignal}
          handoff={mascotHandoff}
        />
      )}
      {topAccessoryVisible && (
        <div
          ref={topAccessoryRef}
          data-testid="composer-top-accessory-overlay"
          style={{
            position: 'absolute',
            insetInline: 0,
            bottom: 'calc(100% - 1px)',
            zIndex: 0,
            pointerEvents: 'none'
          }}
        >
          {topAccessory}
        </div>
      )}
      <div data-composer-card-layer style={{ position: 'relative' }}>
        <div
          aria-hidden
          className={focused ? 'composer-focus-glow' : undefined}
          style={{
            position: 'absolute',
            inset: '-3px',
            borderRadius: '23px',
            background: 'var(--composer-focus-glow)',
            filter: 'blur(8px)',
            opacity: focused ? 0.22 : hovered ? 0.18 : 0,
            // While focused the breathing animation drives opacity, so this
            // transition only governs the hover halo.
            transition: 'opacity 420ms ease',
            zIndex: -1,
            pointerEvents: 'none'
          }}
        />
        <div
          data-composer-card
          style={{
            position: 'relative',
            zIndex: 1,
            ['--composer-overlay-inset' as string]:
              `${COMPOSER_CARD_INLINE_PADDING + COMPOSER_CARD_BORDER}px`,
            border: focused
              ? `${COMPOSER_CARD_BORDER}px solid var(--composer-focus-border)`
              : `${COMPOSER_CARD_BORDER}px solid var(--composer-input-rest-border)`,
            borderRadius: '20px',
            background: 'var(--composer-input-background)',
            padding: `10px ${COMPOSER_CARD_INLINE_PADDING}px 8px`,
            transition: 'border-color 0.2s ease',
            boxShadow: topAccessoryVisible
              ? 'var(--composer-input-shadow), inset 0 1px 0 var(--composer-top-accessory-separator)'
              : 'var(--composer-input-shadow)'
          }}
          onDragOver={onDragOver}
          onDragLeave={onDragLeave}
          onDrop={onDrop}
        >
          {showMascot && !topAccessoryVisible && (
            <ComposerMascotShadow name={renderedMascotAvatar} />
          )}
          {dragOver && (
            <div
              style={{
                position: 'absolute',
                inset: 0,
                zIndex: 20,
                border: '2px dashed var(--accent)',
                borderRadius: '18px',
                background: 'rgba(124, 58, 237, 0.08)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                pointerEvents: 'none',
                fontSize: 'var(--type-ui-size)',
                lineHeight: 'var(--type-ui-line-height)',
                color: 'var(--accent)'
              }}
            >
              {dropLabel}
            </div>
          )}

          <DesktopPluginSurface name="composer.input" context={desktopPluginSurfaceContext}>
            <DesktopPluginSurface name="composer.input.attachments" context={desktopPluginSurfaceContext}>
              {attachmentStrip}
            </DesktopPluginSurface>
            <DesktopPluginSurface name="composer.input.editor" context={desktopPluginSurfaceContext}>
              <OverlayLiftProvider value={overlayLiftApi}>{editor}</OverlayLiftProvider>
            </DesktopPluginSurface>
          </DesktopPluginSurface>

          <DesktopPluginSurface name="composer.toolbar" context={desktopPluginSurfaceContext}>
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
                gap: '10px',
                marginTop: '8px',
                paddingTop: '6px'
              }}
            >
              <div
                data-composer-toolbar-leading
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '10px',
                  minWidth: 0,
                  flex: '1 1 auto'
                }}
              >
                <DesktopPluginSurface name="composer.toolbar.leading" context={desktopPluginSurfaceContext}>
                  {footerLeading}
                </DesktopPluginSurface>
              </div>
              <div
                data-composer-toolbar-trailing
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: '10px',
                  flex: '0 0 auto'
                }}
              >
                <DesktopPluginSurface name="composer.toolbar.trailing" context={desktopPluginSurfaceContext}>
                  {footerAction}
                </DesktopPluginSurface>
              </div>
            </div>
          </DesktopPluginSurface>
        </div>
      </div>
      <DesktopPluginSurface name="composer.status" context={desktopPluginSurfaceContext}>
        {belowFooter}
      </DesktopPluginSurface>
    </div>
  )
}

export function ComposerPlanModeLabel({
  value,
  onDisable,
  label,
  title,
  ariaLabel,
  shortcut
}: ComposerPlanModeLabelProps): JSX.Element | null {
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const active = hovered || focused
  const Icon = active ? X : ListChecks

  if (value !== 'plan') return null

  return (
    <ActionTooltip label={title} shortcut={shortcut} placement="top">
      <button
        type="button"
        onClick={onDisable}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        onFocus={() => setFocused(true)}
        onBlur={() => setFocused(false)}
        aria-label={ariaLabel}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '6px',
          height: COMPOSER_FOOTER_CONTROL_HEIGHT,
          padding: '0 6px',
          borderRadius: '999px',
          border: 'none',
          background: active ? composerFooterControlHoverBackground : 'transparent',
          color: 'var(--composer-footer-text)',
          cursor: 'pointer',
          fontSize: 'var(--type-secondary-size)',
          lineHeight: 'var(--type-secondary-line-height)',
          fontWeight: 'var(--type-ui-emphasis-weight)',
          outline: 'none',
          transition: 'background-color 120ms ease, color 120ms ease'
        }}
    >
        <Icon size={13} strokeWidth={2} aria-hidden />
        <span>{label}</span>
      </button>
    </ActionTooltip>
  )
}

interface ComposerCustomProfileLabelProps {
  label: string
  onClear: () => void
  title: string
  ariaLabel: string
}

export function ComposerCustomProfileLabel({ label, onClear, title, ariaLabel }: ComposerCustomProfileLabelProps): JSX.Element {
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const active = hovered || focused
  const Icon = active ? X : Bot

  return (
    <ActionTooltip label={title} placement="top">
      <button
        type="button"
        onClick={onClear}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        onFocus={() => setFocused(true)}
        onBlur={() => setFocused(false)}
        aria-label={ariaLabel}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '6px',
          height: COMPOSER_FOOTER_CONTROL_HEIGHT,
          padding: '0 6px',
          borderRadius: '999px',
          border: 'none',
          background: active ? composerFooterControlHoverBackground : 'transparent',
          color: 'var(--composer-footer-text)',
          cursor: 'pointer',
          fontSize: 'var(--type-secondary-size)',
          lineHeight: 'var(--type-secondary-line-height)',
          fontWeight: 'var(--type-ui-emphasis-weight)',
          outline: 'none',
          transition: 'background-color 120ms ease, color 120ms ease'
        }}
      >
        <Icon size={13} strokeWidth={2} aria-hidden />
        <span>{label}</span>
      </button>
    </ActionTooltip>
  )
}

export function composerModelPillStyle(color: string, disabled = false): CSSProperties {
  return {
    fontSize: 'var(--type-secondary-size)',
    lineHeight: 'var(--type-secondary-line-height)',
    fontWeight: 'var(--type-ui-emphasis-weight)',
    color,
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    maxWidth: '220px',
    height: COMPOSER_FOOTER_CONTROL_HEIGHT,
    borderRadius: '999px',
    border: 'none',
    backgroundColor: 'transparent',
    padding: '0 4px',
    outline: 'none',
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    opacity: disabled ? 0.72 : 1,
    boxShadow: 'none',
    transition: 'background-color 120ms ease, color 120ms ease'
  }
}

export const composerActionButtonStyle: CSSProperties = {
  width: '32px',
  height: '32px',
  borderRadius: '999px',
  border: 'none',
  flexShrink: 0,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  cursor: 'pointer',
  boxShadow: 'var(--composer-action-shadow)',
  transition: 'background-color 100ms ease'
}

export function composerSendButtonStyle(tone: ComposerActionButtonTone, active = false): CSSProperties {
  const enabled = tone === 'enabled'

  return {
    ...composerActionButtonStyle,
    backgroundColor: enabled
      ? active
        ? '#ffffff'
        : '#f5f6f7'
      : 'color-mix(in srgb, var(--bg-primary) 92%, #ffffff 8%)',
    color: enabled ? '#1f2328' : 'var(--text-dimmed)',
    cursor: enabled ? 'pointer' : 'default'
  }
}

interface ComposerSendButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  tone: ComposerActionButtonTone
}

export function ComposerSendButton({
  tone,
  children,
  onMouseEnter,
  onMouseLeave,
  onFocus,
  onBlur,
  ...props
}: ComposerSendButtonProps): JSX.Element {
  const [active, setActive] = useState(false)
  const enabled = tone === 'enabled' && !props.disabled

  return (
    <button
      {...props}
      type={props.type ?? 'button'}
      onMouseEnter={(event) => {
        if (enabled) setActive(true)
        onMouseEnter?.(event)
      }}
      onMouseLeave={(event) => {
        setActive(false)
        onMouseLeave?.(event)
      }}
      onFocus={(event) => {
        if (enabled && event.currentTarget.matches(':focus-visible')) setActive(true)
        onFocus?.(event)
      }}
      onBlur={(event) => {
        setActive(false)
        onBlur?.(event)
      }}
      style={{
        ...composerSendButtonStyle(tone, active),
        ...props.style
      }}
    >
      {children}
    </button>
  )
}

export function SendIcon(): JSX.Element {
  return (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M12 19a1.25 1.25 0 0 1-1.25-1.25v-8.03l-3.1 3.1a1.25 1.25 0 1 1-1.77-1.77l5.24-5.24a1.25 1.25 0 0 1 1.76 0l5.24 5.24a1.25 1.25 0 1 1-1.77 1.77l-3.1-3.1v8.03A1.25 1.25 0 0 1 12 19Z" />
    </svg>
  )
}

export function SendProcessingIcon(): JSX.Element {
  return <Loader2 size={16} strokeWidth={2.2} className="animate-spin-custom" aria-hidden="true" />
}

export function StopIcon(): JSX.Element {
  return <Square size={12} strokeWidth={0} fill="currentColor" aria-hidden="true" />
}
