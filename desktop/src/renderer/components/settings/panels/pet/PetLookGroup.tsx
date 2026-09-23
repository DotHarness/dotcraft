import type { JSX } from 'react'
import { AppearanceAvatar } from '@dotcraft/avatar/react'
import type { Appearance, ItemId } from '@dotcraft/avatar'
import type { PetSettings } from '../../../../../shared/pet'
import { useT } from '../../../../contexts/LocaleContext'
import { SettingsGroup } from '../../SettingsGroup'
import { PetBagPanel } from './PetBagPanel'
import { PetColourRing, colourKey } from './PetColourRing'
import type { usePetPose } from './usePetPose'

interface PetLookGroupProps {
  appearance: Appearance
  settings: PetSettings
  pose: ReturnType<typeof usePetPose>
  preview: number | null
  onPreview: (palette: number | null) => void
  onPalette: (palette: number) => void
  onToggle: (id: ItemId) => void
  onTryOn: (id: ItemId | null) => void
}

export function PetLookGroup({ appearance, settings, pose, preview, onPreview, onPalette, onToggle, onTryOn }: PetLookGroupProps): JSX.Element {
  const t = useT()
  const trying = preview !== null && preview !== settings.palette
  return (
    <SettingsGroup title={t('settings.pet.look.title')} description={t('settings.pet.look.description')} flush>
      <div className="pet-settings-look">
        <div className="pet-settings-dial">
          <PetColourRing value={settings.palette} onChange={onPalette} onPreview={onPreview} />
          <button type="button" className="pet-settings-figure" aria-label={t('settings.pet.look.figure')} onClick={pose.random}>
            <AppearanceAvatar appearance={appearance} size={120} state={pose.pose} eventSequence={pose.eventSequence} motion="system" />
          </button>
          <span className="pet-settings-dial-label" data-preview={trying} aria-live="polite">
            {t(colourKey(trying ? preview! : settings.palette))}
          </span>
        </div>
        <PetBagPanel settings={settings} onToggle={onToggle} onTryOn={onTryOn} />
      </div>
    </SettingsGroup>
  )
}
