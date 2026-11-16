import type { JSX, ReactNode } from 'react'

export function SegmentSetupState({ icon, title, description, action }: { icon: ReactNode; title: string; description: string; action?: ReactNode }): JSX.Element {
  return <div className="satellite-setup"><span className="satellite-setup-glyph" aria-hidden>{icon}</span><div className="satellite-setup-title">{title}</div><div className="satellite-setup-description">{description}</div>{action && <div className="satellite-setup-action">{action}</div>}</div>
}
