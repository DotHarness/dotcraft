import { useT } from '../../contexts/LocaleContext'
import { useState } from 'react'
import { Eye, EyeOff } from 'lucide-react'
import { IconButton } from '../ui/IconButton'
import { Input } from '../ui/Input'

interface SecretInputProps {
  id?: string
  value: string
  placeholder?: string
  ariaLabel?: string
  disabled?: boolean
  onChange: (value: string) => void
  /** Monospace value — for keys, tokens, and other literal secrets. */
  mono?: boolean
}

export function SecretInput({
  id,
  value,
  placeholder,
  ariaLabel,
  disabled = false,
  onChange,
  mono = false
}: SecretInputProps): JSX.Element {
  const t = useT()
  const [visible, setVisible] = useState(false)

  return (
    <div style={{ position: 'relative' }}>
      <Input
        id={id}
        type={visible ? 'text' : 'password'}
        value={value}
        placeholder={placeholder}
        aria-label={ariaLabel}
        disabled={disabled}
        mono={mono}
        onChange={(event) => onChange(event.target.value)}
        style={{ paddingRight: '36px' }}
      />
      <IconButton
        size={24}
        radius={6}
        label={visible ? t('common.hideSecret') : t('common.showSecret')}
        aria-pressed={visible}
        disabled={disabled}
        onClick={() => setVisible((current) => !current)}
        style={{
          position: 'absolute',
          right: '8px',
          top: '50%',
          transform: 'translateY(-50%)'
        }}
        icon={visible ? <EyeOff size={14} aria-hidden /> : <Eye size={14} aria-hidden />}
      />
    </div>
  )
}
