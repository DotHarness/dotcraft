import { useState, type JSX } from 'react'
import { Globe } from 'lucide-react'
import { useLinkPreferencesStore } from '../../stores/linkPreferencesStore'
import { siteFaviconUrl } from '../../utils/webLinks'

export function LinkFavicon({ url }: { url: string }): JSX.Element {
  const enabled = useLinkPreferencesStore((state) => state.preferences.favicons)
  const src = enabled ? siteFaviconUrl(url) : null
  const [loaded, setLoaded] = useState<string | null>(null)
  const [failed, setFailed] = useState<string | null>(null)

  return (
    <span className="dc-ref-favicon" aria-hidden>
      {(!src || loaded !== src) && <Globe size={12} strokeWidth={2.1} />}
      {src && failed !== src && (
        <img
          src={src}
          alt=""
          draggable={false}
          referrerPolicy="no-referrer"
          data-loaded={loaded === src ? 'true' : undefined}
          onLoad={() => setLoaded(src)}
          onError={() => setFailed(src)}
        />
      )}
    </span>
  )
}
