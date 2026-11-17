import { useEffect, useState, type ReactNode, type Ref } from 'react'

const MORPH_MS = 420

export interface IslandHeadProps {
  open: boolean
  onToggle: () => void
  disabled?: boolean
  expandLabel: string
  collapseLabel: string
  children: ReactNode
}

export function IslandHead({ open, onToggle, disabled = false, expandLabel, collapseLabel, children }: IslandHeadProps) {
  return (
    <button
      className="dci-head"
      type="button"
      disabled={disabled}
      aria-expanded={disabled ? undefined : open}
      aria-label={disabled ? undefined : open ? collapseLabel : expandLabel}
      onClick={onToggle}
    >
      {children}
    </button>
  )
}

export interface IslandBodyContent {
  key: string
  node: ReactNode
}

/** A closing body stays until the island has shrunk, so its height never snaps. */
export function IslandBody({ body }: { body: IslandBodyContent | null }) {
  const [held, setHeld] = useState(body)
  const key = body?.key
  useEffect(() => {
    if (body) { setHeld(body); return undefined }
    const timer = setTimeout(() => setHeld(null), MORPH_MS)
    return () => clearTimeout(timer)
  }, [key])
  const shown = body ?? held
  if (!shown) return null
  return (
    <div className="dci-body" key={shown.key} data-leaving={body ? undefined : ''} inert={!body}>
      {shown.node}
    </div>
  )
}

export interface IslandChoice {
  id: string
  label: string
  description?: string
  primary?: boolean
}

export interface IslandChoicesProps {
  title: string
  detail?: ReactNode
  choices: IslandChoice[]
  onChoose: (id: string) => void
  firstRef?: Ref<HTMLButtonElement>
}

export function IslandChoices({ title, detail, choices, onChoose, firstRef }: IslandChoicesProps) {
  return (
    <div className="dci-choices">
      <p className="dci-choices__title">{title}</p>
      {detail && <div className="dci-choices__detail">{detail}</div>}
      <div className="dci-choices__options" role="group" aria-label={title}>
        {choices.map((choice, index) => (
          <button
            key={choice.id}
            ref={index === 0 ? firstRef : undefined}
            type="button"
            data-primary={choice.primary || undefined}
            onClick={() => onChoose(choice.id)}
          >
            <span>{choice.label}</span>
            {choice.description && <small>{choice.description}</small>}
          </button>
        ))}
      </div>
    </div>
  )
}
