import { ChevronDown, ChevronRight } from 'lucide-react'

interface DisclosureChevronProps {
  expanded: boolean
  direction?: 'inline' | 'reveal'
}

export function DisclosureChevron({ expanded, direction = 'inline' }: DisclosureChevronProps): JSX.Element {
  const Icon = direction === 'reveal' ? ChevronDown : ChevronRight
  return (
    <Icon
      className="dc-disclosure-chevron"
      data-direction={direction}
      data-expanded={expanded ? 'true' : undefined}
      size={14}
      strokeWidth={1.8}
      aria-hidden
    />
  )
}
