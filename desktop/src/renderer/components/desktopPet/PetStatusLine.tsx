import type { PetLineTone } from '../../../shared/desktopPet'
import { RunningShimmer } from '../ui/RunningShimmer'

export function PetStatusLine({ line, tone, running, reducedMotion }: {
  line: string
  tone: PetLineTone
  running: boolean
  reducedMotion: boolean
}): JSX.Element {
  return <RunningShimmer as="div" active={running} reducedMotion={reducedMotion} className="desktop-pet-pill-line"
    data-tone={tone} data-wrap={tone === 'danger' ? 'true' : undefined} title={line}>{line}</RunningShimmer>
}
