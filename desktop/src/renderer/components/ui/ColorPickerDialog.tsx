import {
  useEffect,
  useId,
  useRef,
  useState,
  type FormEvent,
  type JSX
} from 'react'
import { createPortal } from 'react-dom'
import { Palette } from 'lucide-react'
import { HexColorPicker } from 'react-colorful'
import type {
  DesktopPluginColorPickerOptions,
  DesktopPluginColorPickerResult
} from '@dotcraft/plugin'
import { normalizeHexColor } from '../../../shared/themeSeed'
import { LayerBoundary } from '../../contexts/LayerContext'
import { useT } from '../../contexts/LocaleContext'
import { Button } from './Button'
import { Input } from './Input'
import { ModalHeader } from './ModalHeader'

interface ColorPickerDialogProps {
  options: DesktopPluginColorPickerOptions
  initialDraft?: string
  onFinish(result: DesktopPluginColorPickerResult): void
}

interface ColorPickerDialogRequest {
  result: Promise<DesktopPluginColorPickerResult>
  dismiss(): void
}

interface ActiveRequest {
  id: number
  options: DesktopPluginColorPickerOptions
  returnFocus: HTMLElement | null
  resolve(result: DesktopPluginColorPickerResult): void
}

let activeRequest: ActiveRequest | null = null
let requestListener: ((request: ActiveRequest | null) => void) | null = null
let nextRequestId = 0

export function ColorPickerDialog({
  options,
  initialDraft,
  onFinish
}: ColorPickerDialogProps): JSX.Element {
  const t = useT()
  const titleId = useId()
  const inputId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const pickerRef = useRef<HTMLDivElement>(null)
  const initialColor = normalizeHexColor(options.initialColor)!
  const [input, setInput] = useState(initialDraft ?? initialColor.toUpperCase())
  const [color, setColor] = useState(initialColor)
  const validColor = normalizeHexColor(input)

  useEffect(() => {
    inputRef.current?.focus()
    inputRef.current?.select()
  }, [])

  useEffect(() => {
    const sliders = pickerRef.current?.querySelectorAll<HTMLElement>('[role="slider"]')
    sliders?.[0]?.setAttribute('aria-label', t('colorPicker.saturation'))
    sliders?.[1]?.setAttribute('aria-label', t('colorPicker.hue'))
  }, [t])

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') {
        event.preventDefault()
        onFinish({ kind: 'cancel' })
        return
      }
      if (event.key !== 'Tab' || !dialogRef.current) return
      const focusable = Array.from(dialogRef.current.querySelectorAll<HTMLElement>(
        'button:not([disabled]), input:not([disabled]), [role="slider"][tabindex="0"]'
      ))
      if (focusable.length === 0) return
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onFinish])

  function submit(event: FormEvent): void {
    event.preventDefault()
    if (validColor) onFinish({ kind: 'select', color: validColor })
  }

  const dialog = (
    <div
      className="dc-color-picker-overlay"
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onFinish({ kind: 'cancel' })
      }}
    >
      <div
        ref={dialogRef}
        className="dc-color-picker-dialog"
        onMouseDown={(event) => event.stopPropagation()}
      >
        <ModalHeader
          icon={<Palette size={18} aria-hidden />}
          title={options.title}
          titleId={titleId}
          description={options.description}
          onClose={() => onFinish({ kind: 'cancel' })}
          closeLabel={t('common.close')}
        />
        <form onSubmit={submit}>
          <div className="dc-color-picker-value-row">
            <span
              className="dc-color-picker-preview"
              style={{ background: color }}
              aria-hidden
            />
            <label className="dc-color-picker-field" htmlFor={inputId}>
              <span>{t('colorPicker.hex')}</span>
              <Input
                ref={inputRef}
                id={inputId}
                mono
                invalid={validColor === null}
                spellCheck={false}
                value={input}
                onChange={(event) => {
                  const next = event.currentTarget.value
                  setInput(next)
                  const normalized = normalizeHexColor(next)
                  if (normalized) setColor(normalized)
                }}
              />
            </label>
          </div>
          {validColor === null && (
            <p className="dc-color-picker-error" role="alert">
              {t('colorPicker.invalid')}
            </p>
          )}
          <div ref={pickerRef} className="dc-color-picker-control">
            <HexColorPicker
              color={color}
              onChange={(next) => {
                const normalized = normalizeHexColor(next) ?? color
                setColor(normalized)
                setInput(normalized.toUpperCase())
              }}
            />
          </div>
          <div className="dc-color-picker-actions">
            {options.allowReset ? (
              <Button
                type="button"
                variant="secondary"
                onClick={() => onFinish({ kind: 'reset' })}
              >
                {t('colorPicker.reset')}
              </Button>
            ) : <span />}
            <Button type="submit" variant="primary" disabled={validColor === null}>
              {t('colorPicker.done')}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )

  return createPortal(<LayerBoundary>{dialog}</LayerBoundary>, document.body) as JSX.Element
}

export function ColorPickerDialogHost(): JSX.Element | null {
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
  return (
    <ColorPickerDialog
      key={request.id}
      options={request.options}
      onFinish={(result) => finishRequest(request, result)}
    />
  )
}

export function requestColorPickerDialog(
  rawOptions: DesktopPluginColorPickerOptions
): ColorPickerDialogRequest {
  const options = validateOptions(rawOptions)
  if (activeRequest) {
    return {
      result: Promise.resolve({ kind: 'cancel' }),
      dismiss() {}
    }
  }

  const id = ++nextRequestId
  let resolve!: (result: DesktopPluginColorPickerResult) => void
  const result = new Promise<DesktopPluginColorPickerResult>((settle) => {
    resolve = settle
  })
  const request: ActiveRequest = {
    id,
    options,
    returnFocus: document.activeElement instanceof HTMLElement ? document.activeElement : null,
    resolve
  }
  activeRequest = request
  requestListener?.(request)
  return {
    result,
    dismiss() {
      finishRequest(request, { kind: 'cancel' })
    }
  }
}

function finishRequest(
  request: ActiveRequest,
  result: DesktopPluginColorPickerResult
): void {
  if (activeRequest?.id !== request.id) return
  activeRequest = null
  requestListener?.(null)
  request.resolve(result)
  requestAnimationFrame(() => request.returnFocus?.focus())
}

function validateOptions(
  raw: DesktopPluginColorPickerOptions
): DesktopPluginColorPickerOptions {
  if (!raw || typeof raw !== 'object') throw new TypeError('Color picker options must be an object.')
  if (typeof raw.title !== 'string' || !raw.title.trim()) {
    throw new TypeError('Color picker title must be a non-empty string.')
  }
  if (raw.description !== undefined && typeof raw.description !== 'string') {
    throw new TypeError('Color picker description must be a string.')
  }
  const initialColor = normalizeHexColor(raw.initialColor)
  if (!initialColor) throw new TypeError('Color picker initialColor must be a valid hex color.')
  if (raw.allowReset === true) {
    const defaultColor = normalizeHexColor(raw.defaultColor)
    if (!defaultColor) throw new TypeError('A resettable color picker requires a valid defaultColor.')
    return { ...raw, initialColor, defaultColor }
  }
  if (raw.allowReset !== undefined && raw.allowReset !== false) {
    throw new TypeError('Color picker allowReset must be a boolean.')
  }
  if (raw.defaultColor !== undefined) {
    throw new TypeError('Color picker defaultColor requires allowReset: true.')
  }
  return { ...raw, initialColor, allowReset: false }
}
