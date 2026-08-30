import type { SegmentedControlProps } from '@dotcraft/plugin'
import type { JSX } from 'react'

import { SegmentedControl } from '../settings/ui/SegmentedControl'

/** The kit names value callbacks `onValueChange`; Core keeps `onChange` for its own call sites. */
export function DesktopPluginSegmentedControl<T extends string>({
  options,
  onValueChange,
  ...props
}: SegmentedControlProps<T>): JSX.Element {
  return <SegmentedControl {...props} options={[...options]} onChange={onValueChange} />
}
