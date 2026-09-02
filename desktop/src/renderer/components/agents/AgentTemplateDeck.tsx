import { useRef, useState, type CSSProperties, type JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { RobotAvatar } from './RobotAvatar'
import type { AvatarSpec } from './agentAvatar'
import './AgentTemplateDeck.css'

export interface AgentTemplateDeckItem {
  key: string
  name: string
  description: string
  avatar: AvatarSpec
}

interface AgentTemplateDeckProps {
  templates: AgentTemplateDeckItem[]
  onPick: (key: string) => void
}

const MAX_CARDS = 5
const FAN_STEP_DEG = 6
const FAN_DROP_PX = 7
const FAN_SPREAD_PX = 14

/** The Welcome page's hand of template cards; the one under the pointer rises while its neighbours part. */
export function AgentTemplateDeck({ templates, onPick }: AgentTemplateDeckProps): JSX.Element | null {
  const t = useT()
  const deckRef = useRef<HTMLDivElement>(null)
  const [active, setActive] = useState<number | null>(null)
  const cards = templates.slice(0, MAX_CARDS)
  const mid = (cards.length - 1) / 2

  if (cards.length === 0) return null

  // Selection follows pointer X over the stable deck rect rather than per-card :hover,
  // so a card rising under the pointer cannot hand the selection to its neighbour.
  const trackPointer = (clientX: number): void => {
    const el = deckRef.current
    if (!el) return
    const rect = el.getBoundingClientRect()
    if (rect.width <= 0) return
    const ratio = (clientX - rect.left) / rect.width
    setActive(Math.max(0, Math.min(cards.length - 1, Math.floor(ratio * cards.length))))
  }

  return (
    <div
      ref={deckRef}
      className="agent-template-deck"
      onMouseMove={(e) => trackPointer(e.clientX)}
      onMouseLeave={() => setActive(null)}
      onBlur={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget as Node | null)) setActive(null)
      }}
    >
      {cards.map((card, i) => {
        const offset = i - mid
        const isActive = active === i
        const spread = active === null || isActive ? 0 : Math.sign(i - active) * FAN_SPREAD_PX
        const style = {
          '--deck-i': i,
          '--deck-rot': `${offset * FAN_STEP_DEG}deg`,
          '--deck-y': `${Math.abs(offset) * Math.abs(offset) * FAN_DROP_PX}px`,
          '--deck-x': `${spread}px`,
          zIndex: isActive ? cards.length + 1 : cards.length - Math.round(Math.abs(offset))
        } as CSSProperties
        return (
          <button
            key={card.key}
            type="button"
            className={`agent-template-card${isActive ? ' is-active' : ''}`}
            style={style}
            data-testid="agent-template-card"
            onFocus={() => setActive(i)}
            onClick={() => onPick(card.key)}
          >
            <span className="agent-template-card-avatar">
              <RobotAvatar spec={card.avatar} size={44} />
            </span>
            <span className="agent-template-card-name">{card.name}</span>
            <span className="agent-template-card-desc">{card.description}</span>
            <span className="agent-template-card-cta" aria-hidden>{t('agentBuilder.intro.useTemplate')}</span>
          </button>
        )
      })}
    </div>
  )
}
