import {
  useLayoutEffect,
  useRef,
  useState,
  type ButtonHTMLAttributes,
  type ComponentProps,
  type CSSProperties,
  type DragEventHandler,
  type JSX,
  type ReactNode
} from 'react'
import { Bot, CircleX, Hammer, ListChecks, Play, Square } from 'lucide-react'
import { Spinner } from '../ui/Spinner'
import type {
  DesktopPluginComposerSurfaceContext,
} from '@dotcraft/plugin'
import { ActionTooltip } from '../ui/ActionTooltip'
import { DesktopPluginSurface } from '../desktopPlugins/DesktopPluginSurface'
import { AvatarOutfitProvider, ComposerMascot, ComposerMascotShadow, type MascotExpression, type MascotLight } from '@dotcraft/avatar/react'
import { MascotBubble, type MascotBubbleAction, type MascotBubbleTone } from './MascotBubble'
import { useComposerOverlayLiftHost } from './composerOverlayLift'
import { ContextMenu, type ContextMenuItem } from '../ui/ContextMenu'
import type { ShortcutSpec } from '../ui/shortcutKeys'
import { useDocumentThemeMode } from '../../utils/theme'
import { useComposerEffectsMode, useComposerTypingEffect } from '../../utils/appearance'
import { ComposerInputEffects } from './inputEffects/ComposerInputEffects'
import { ComposerTypingEffects } from './inputEffects/ComposerTypingEffects'
import { useDesktopPet } from '../desktopPet/useDesktopPet'
import type { PetSourceSurface } from '../desktopPet/desktopPetSource'
import { usePetStore } from '../../pet/petStore'
import { useAdaptiveComposerToolbar } from './useAdaptiveComposerToolbar'
import { useMascotOutfitTransition } from './useMascotOutfitTransition'
import { useMascotVoiceLink } from './inputEffects/useMascotVoiceLink'
import { ComposerGiftStack } from './mascotGifts/ComposerGiftStack'
import { useGiftStore } from '../../pet/giftStore'
import css from './ComposerShell.module.css'

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
  guide?: ReactNode
  menuItems?: ContextMenuItem[]
  hold?: 'sign'
}

export type ComposerMascotReasoningEffort = 'off' | 'low' | 'medium' | 'high' | 'extraHigh' | 'max'
export type ComposerMascotSpeed = 'standard' | 'fast'

export const DECISION_MASCOT: ComposerMascotInteraction = { expression: 'operator', hold: 'sign' }

type ComposerActionButtonTone = 'enabled' | 'disabled'

export const COMPOSER_FOOTER_CONTROL_HEIGHT = 24

export const composerFooterControlBoxStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  height: COMPOSER_FOOTER_CONTROL_HEIGHT
}

interface ComposerShellProps {
  before?: ReactNode
  dragOver: boolean
  dropLabel: string
  topAccessory?: ReactNode
  topAccessoryVisible?: boolean
  attachmentStrip?: ReactNode
  editor: ReactNode
  footerLeading: ReactNode
  footerAction: ReactNode
  adaptiveToolbar?: boolean
  inputEffectsVoiceOrigin?: string
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
  mascotName?: string
  mascotOutfit?: Omit<ComponentProps<typeof AvatarOutfitProvider>, 'children'> & { accent?: string }
  mascotOutfitSignal?: number
  mascotHandoff?: boolean
  /** How the desktop pet treats this composer; defaults to chat when it can chat, else decision. */
  petSurface?: PetSourceSurface
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
  before,
  dragOver,
  dropLabel,
  topAccessory,
  topAccessoryVisible = false,
  attachmentStrip,
  editor,
  footerLeading,
  footerAction,
  adaptiveToolbar = false,
  inputEffectsVoiceOrigin,
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
  mascotName,
  mascotOutfit,
  mascotOutfitSignal = 0,
  mascotHandoff = false,
  petSurface
}: ComposerShellProps): JSX.Element {
  const mascotTheme = useDocumentThemeMode() === 'dark' ? 'dark' : 'light'
  const effectsMode = useComposerEffectsMode()
  const showInputEffects = inputEffectsVoiceOrigin !== undefined && effectsMode === 'colorful'
  const typingEffect = useComposerTypingEffect()
  const petAppearance = usePetStore((state) => state.appearance)
  const petRoot = useRef<HTMLDivElement>(null)
  const [renderedMascotAvatar, setRenderedMascotAvatar] = useState(mascotName)
  const canChat = !desktopPluginSurfaceContext.awaitingApproval && mascotInteraction?.hold !== 'sign'
  useDesktopPet(petRoot, showMascot, petSurface ?? (canChat ? 'chat' : 'decision'), {
    threadId: desktopPluginSurfaceContext.threadId,
    mascotName: renderedMascotAvatar ?? ''
  })
  const outfit = useMascotOutfitTransition(mascotOutfit, mascotOutfitSignal)
  const outfitAccent = useRef<string | undefined>(undefined)
  if (mascotOutfit?.accent) outfitAccent.current = mascotOutfit.accent
  const voiceLink = useMascotVoiceLink(petRoot, inputEffectsVoiceOrigin, showMascot && showInputEffects)
  const giftOpenRevision = useGiftStore((state) => state.openRevision)
  const { toolbarRef, hidden } = useAdaptiveComposerToolbar(adaptiveToolbar, [footerLeading, footerAction])
  const [topAccessoryHeight, setTopAccessoryHeight] = useState(0)
  const [topAccessoryPushSignal, setTopAccessoryPushSignal] = useState(0)
  const { lift: overlayLift, api: overlayLiftApi, Provider: OverlayLiftProvider } =
    useComposerOverlayLiftHost()
  const topAccessoryRef = useRef<HTMLDivElement | null>(null)
  const topAccessoryHeightRef = useRef(0)
  const beforeRef = useRef<HTMLDivElement | null>(null)
  const [beforeHeight, setBeforeHeight] = useState(0)

  useLayoutEffect(() => {
    const element = beforeRef.current
    if (!element) return undefined
    const measure = (): void => setBeforeHeight(Math.round(element.getBoundingClientRect().height))
    measure()
    if (typeof ResizeObserver === 'undefined') return undefined
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [before])

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
    <>
    {before !== undefined && <div ref={beforeRef}>{before}</div>}
    <div
      data-composer-root
      className={adaptiveToolbar ? css.adaptiveRoot : undefined}
      ref={petRoot}
      style={{
        position: 'relative',
        padding: belowFooter ? 0 : '0 0 14px',
        display: 'flex',
        flexDirection: 'column',
        gap: 0,
        opacity,
        isolation: 'isolate',
        ['--mascot-outfit-accent' as string]: outfitAccent.current,
        // Lets menus and tooltips that open upward cover the conversation's floating controls.
        zIndex: 20
      }}
    >
      {showMascot && (
        <ComposerMascot
          theme={mascotTheme}
          focused={focused}
          dragOver={dragOver}
          bounceSignal={mascotBounceSignal}
          interaction={mascotInteraction ? { ...mascotInteraction, bubble: mascotInteraction.guide ?? (mascotInteraction.bubble ? <MascotBubble {...mascotInteraction.bubble} /> : undefined) } : undefined}
          renderMenu={mascotInteraction?.menuItems?.length ? (position, close) => <ContextMenu items={mascotInteraction.menuItems!} position={position} onClose={close} /> : undefined}
          renderCharacter={(character, context) => <DesktopPluginSurface name="composer.mascot" context={{ ...desktopPluginSurfaceContext, ...context }}><div className="composer-mascot-outfit" data-mascot-outfit-phase={outfit.phase ?? undefined}>{outfit.outfit ? <AvatarOutfitProvider pack={outfit.outfit.pack} wear={outfit.outfit.wear}>{character}</AvatarOutfitProvider> : character}</div></DesktopPluginSurface>}
          reasoningEffort={mascotReasoningEffort}
          speed={mascotSpeed}
          name={mascotName}
          appearance={petAppearance}
          onNameRendered={setRenderedMascotAvatar}
          anchorOffset={Math.max(topAccessoryHeight, overlayLift, beforeHeight)}
          anchorPushSignal={topAccessoryPushSignal}
          handoff={mascotHandoff}
          beside={<ComposerGiftStack />}
          listening={voiceLink.listening}
          catchSignal={voiceLink.catchSignal}
          celebrateSignal={giftOpenRevision}
          outfitSignal={outfit.spin}
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
          data-composer-card
          className={adaptiveToolbar ? css.adaptiveCard : undefined}
          style={{
            position: 'relative',
            zIndex: 1,
            ['--composer-overlay-inset' as string]:
              `${COMPOSER_CARD_INLINE_PADDING + COMPOSER_CARD_BORDER}px`,
            border: `${COMPOSER_CARD_BORDER}px solid var(--composer-input-rest-border)`,
            borderRadius: '20px',
            background: 'var(--composer-input-background)',
            padding: `10px ${COMPOSER_CARD_INLINE_PADDING}px 8px`,
            boxShadow: topAccessoryVisible
              ? 'var(--composer-input-shadow), inset 0 1px 0 var(--composer-top-accessory-separator)'
              : 'var(--composer-input-shadow)'
          }}
          onDragOver={onDragOver}
          onDragLeave={onDragLeave}
          onDrop={onDrop}
        >
          {showInputEffects && (
            <ComposerInputEffects key={inputEffectsVoiceOrigin} origin={inputEffectsVoiceOrigin} />
          )}
          {showInputEffects && typingEffect !== 'off' && (
            <ComposerTypingEffects origin={inputEffectsVoiceOrigin} effect={typingEffect} />
          )}
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
              ref={toolbarRef}
              className={adaptiveToolbar ? `${css.toolbar} ${css.adaptiveToolbar}` : css.toolbar}
              data-hide-context={hidden.context || undefined}
              data-hide-model={hidden.model || undefined}
              data-hide-goal={hidden.goal || undefined}
              data-hide-mode={hidden.mode || undefined}
              style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'space-between',
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
    </>
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
  const Icon = active ? CircleX : ListChecks

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
        className="dc-composer-pill"
        style={composerModePillStyle}
    >
        <Icon size={13} strokeWidth={2} aria-hidden />
        <span>{label}</span>
      </button>
    </ActionTooltip>
  )
}

interface ComposerCreatorLabelProps {
  onDisable: () => void
  label: string
  title: string
  ariaLabel: string
}

export function ComposerCreatorLabel({
  onDisable,
  label,
  title,
  ariaLabel
}: ComposerCreatorLabelProps): JSX.Element {
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const Icon = hovered || focused ? CircleX : Hammer

  return (
    <ActionTooltip label={title} placement="top">
      <button
        type="button"
        onClick={onDisable}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        onFocus={() => setFocused(true)}
        onBlur={() => setFocused(false)}
        aria-label={ariaLabel}
        className="dc-composer-pill"
        style={composerModePillStyle}
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
  const Icon = active ? CircleX : Bot

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
        className="dc-composer-pill"
        style={composerModePillStyle}
      >
        <Icon size={13} strokeWidth={2} aria-hidden />
        <span>{label}</span>
      </button>
    </ActionTooltip>
  )
}

export const composerModePillStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '6px',
  height: COMPOSER_FOOTER_CONTROL_HEIGHT,
  padding: '0 8px',
  borderRadius: '999px',
  border: 'none',
  color: 'var(--composer-footer-text)',
  cursor: 'pointer',
  fontSize: 'var(--type-ui-size)',
  lineHeight: 'var(--type-ui-line-height)',
  fontWeight: 'var(--type-ui-emphasis-weight)'
}

export function composerModelPillStyle(color: string, disabled = false): CSSProperties {
  return {
    fontSize: 'var(--type-ui-size)',
    lineHeight: 'var(--type-ui-line-height)',
    fontWeight: 'var(--type-ui-emphasis-weight)',
    color,
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    maxWidth: '220px',
    height: COMPOSER_FOOTER_CONTROL_HEIGHT,
    borderRadius: '999px',
    border: 'none',
    padding: '0 4px',
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    opacity: disabled ? 0.72 : 1
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
      data-composer-exit-target="primary"
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
  return <Spinner size={16} />
}

export function StopIcon(): JSX.Element {
  return <Square size={12} strokeWidth={0} fill="currentColor" aria-hidden="true" />
}

export function ResumeIcon(): JSX.Element {
  return <Play size={14} strokeWidth={0} fill="currentColor" aria-hidden="true" />
}
