import type { PetLineTone } from '../../../shared/desktopPet'

export function PetStatusLine({ line, tone, running }: { line: string; tone: PetLineTone; running: boolean }): JSX.Element {
  return <div className={running ? 'desktop-pet-pill-line tool-running-gradient-text' : 'desktop-pet-pill-line'}
    data-tone={tone} data-wrap={tone === 'danger' ? 'true' : undefined} title={line}>{line}</div>
}
