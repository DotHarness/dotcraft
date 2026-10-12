import type { SplitButtonProps } from '@dotcraft/plugin'
import type { JSX } from 'react'

import { SplitButton } from '../ui/SplitButton'

export function DesktopPluginSplitButton({ items, ...props }: SplitButtonProps): JSX.Element {
  return (
    <SplitButton
      {...props}
      items={items.map((item, index) => ({
        key: String(index),
        label: item.label,
        icon: item.icon,
        disabled: item.disabled,
        onClick: item.onSelect
      }))}
    />
  )
}
