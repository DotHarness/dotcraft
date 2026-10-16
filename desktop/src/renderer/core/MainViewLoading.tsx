import type { JSX } from 'react'
import { Spinner } from '../components/ui/Spinner'

export function MainViewLoading(): JSX.Element {
  return (
    <div className="dc-delayed-reveal" style={{ flex: 1, display: 'flex', alignItems: 'center', justifyContent: 'center' }}>
      <Spinner size={16} />
    </div>
  )
}
