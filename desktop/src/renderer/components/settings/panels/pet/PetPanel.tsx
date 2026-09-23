import { useMemo, useState, type JSX } from 'react'
import { itemOf, type Appearance, type ItemId } from '@dotcraft/avatar'
import { useT } from '../../../../contexts/LocaleContext'
import { appearanceOf, isWorn, wearItem } from '../../../../pet/petModel'
import { usePetStore } from '../../../../pet/petStore'
import { PillSwitch } from '../../../ui/PillSwitch'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { SettingsPanelShell } from '../../SettingsPanelShell'
import { PetExchangeGroup } from './PetExchangeGroup'
import { PetLookGroup } from './PetLookGroup'
import { usePetPose } from './usePetPose'

export function PetPanel(): JSX.Element {
  const t = useT()
  const settings = usePetStore((state) => state.settings)
  const appearance = usePetStore((state) => state.appearance)
  const setCustomization = usePetStore((state) => state.setCustomization)
  const setPalette = usePetStore((state) => state.setPalette)
  const wear = usePetStore((state) => state.wear)
  const takeOff = usePetStore((state) => state.takeOff)
  const [tryOn, setTryOn] = useState<ItemId | null>(null)
  const [tryColour, setTryColour] = useState<number | null>(null)
  const pose = usePetPose()
  const on = settings.customization
  const shown = useMemo<Appearance>(() => {
    const look = tryOn ? appearanceOf(wearItem(settings, tryOn)) : appearance
    return tryColour === null ? look : { ...look, palette: tryColour }
  }, [appearance, settings, tryColour, tryOn])
  const toggle = (id: ItemId): void => {
    if (isWorn(settings.outfit, id)) takeOff(itemOf(id).slot)
    else wear(id)
    setTryOn(null)
  }
  return (
    <SettingsPanelShell title={t('settings.tab.pet')} description={t('settings.pet.description')}>
      <SettingsGroup>
        <SettingsRow
          label={t('settings.pet.customization.label')}
          description={t('settings.pet.customization.description')}
          control={<PillSwitch checked={on} aria-label={t('settings.pet.customization.label')} onChange={setCustomization} />}
        />
      </SettingsGroup>
      {on && <>
        <PetLookGroup appearance={shown} settings={settings} pose={pose} preview={tryColour} onPreview={setTryColour}
          onPalette={(palette) => { setPalette(palette); setTryColour(null) }} onToggle={toggle} onTryOn={setTryOn} />
        <PetExchangeGroup settings={settings} onWear={wear} />
      </>}
    </SettingsPanelShell>
  )
}
