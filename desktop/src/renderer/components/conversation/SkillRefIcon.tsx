import { Box } from 'lucide-react'
import type { JSX } from 'react'
import { useSkillIconUrl } from '../../utils/skillIcon'

export function SkillRefIcon({ name }: { name: string }): JSX.Element {
  const iconUrl = useSkillIconUrl(name)
  return iconUrl
    ? <img className="dc-ref-skill-image" src={iconUrl} alt="" aria-hidden />
    : <Box size={12} strokeWidth={2.25} aria-hidden />
}
