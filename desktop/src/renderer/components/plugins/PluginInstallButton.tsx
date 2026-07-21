import type { JSX } from 'react'
import { Button, type ButtonProps } from '../ui/Button'

/** Compact pill reserved for actions that install a plugin package. */
export function PluginInstallButton({ className, ...props }: Omit<ButtonProps, 'size'>): JSX.Element {
  return (
    <Button
      {...props}
      size="sm"
      className={className ? `dc-plugin-install-button ${className}` : 'dc-plugin-install-button'}
    />
  )
}
