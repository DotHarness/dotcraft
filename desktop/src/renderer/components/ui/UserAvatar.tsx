import { useEffect, useMemo, useState } from 'react'
import { User } from 'lucide-react'
import { useProfileStore } from '../../stores/profileStore'

export function UserAvatar({
  name,
  avatarUrl,
  size,
  neutral = false
}: {
  name: string
  avatarUrl: string | null
  size: number
  neutral?: boolean
}): JSX.Element {
  const [failed, setFailed] = useState(false)
  const initials = useMemo(() => deriveInitials(name), [name])

  if (avatarUrl && !failed) {
    return (
      <img
        src={avatarUrl}
        alt={name}
        width={size}
        height={size}
        onError={() => setFailed(true)}
        style={{ width: size, height: size, borderRadius: '50%', objectFit: 'cover', flexShrink: 0 }}
      />
    )
  }
  return (
    <div
      aria-hidden
      style={{
        width: size,
        height: size,
        borderRadius: '50%',
        flexShrink: 0,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: neutral ? 'var(--bg-tertiary)' : 'var(--accent)',
        color: neutral ? 'var(--text-secondary)' : 'var(--on-accent)',
        fontSize: Math.round(size / 3),
        fontWeight: 600
      }}
    >
      {initials || <User size={Math.round(size * 0.6)} />}
    </div>
  )
}

let identityRequest: Promise<void> | null = null

export function CurrentUserAvatar({ size }: { size: number }): JSX.Element {
  const identityLoaded = useProfileStore((s) => s.identityLoaded)
  const githubUsername = useProfileStore((s) => s.githubUsername)
  const githubProfile = useProfileStore((s) => s.githubProfile)

  useEffect(() => {
    if (identityLoaded || identityRequest) return
    identityRequest = useProfileStore
      .getState()
      .loadIdentity()
      .finally(() => {
        identityRequest = null
      })
  }, [identityLoaded])

  const name = githubProfile?.name ?? (githubUsername ? `@${githubUsername}` : '')
  return <UserAvatar name={name} avatarUrl={githubProfile?.avatarUrl ?? null} size={size} neutral />
}

function deriveInitials(name: string): string {
  const cleaned = name.replace(/^@/, '').trim()
  if (!cleaned) return ''
  const parts = cleaned.split(/[\s_-]+/).filter(Boolean)
  if (parts.length >= 2) return (parts[0][0] + parts[1][0]).toUpperCase()
  return cleaned.slice(0, 2).toUpperCase()
}
