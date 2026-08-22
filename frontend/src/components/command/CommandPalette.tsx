import { Command } from 'cmdk'
import { Loader2, Search } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useTrackSearch } from '@/api/queries'
import { TrackArt } from '@/components/data/TrackArt'
import { useDebouncedValue } from '@/hooks/use-debounced-value'
import { formatDuration, formatInteger } from '@/lib/format'

interface CommandPaletteProps {
  open: boolean
  onOpenChange: (open: boolean) => void
}

const MIN_QUERY_LENGTH = 2
const RESULT_LIMIT = 8

export function CommandPalette({ open, onOpenChange }: CommandPaletteProps) {
  const [term, setTerm] = useState('')
  const navigate = useNavigate()
  const debouncedTerm = useDebouncedValue(term, 220)
  const canSearch = debouncedTerm.trim().length >= MIN_QUERY_LENGTH

  const resource = useTrackSearch(debouncedTerm.trim(), RESULT_LIMIT, open && canSearch)

  useEffect(() => {
    if (!open) {
      setTerm('')
    }
  }, [open])

  const isSearching = canSearch && resource.state.status === 'loading'
  const items = resource.state.status === 'ready' ? resource.state.data.items : []
  const totalCount = resource.state.status === 'ready' ? resource.state.data.totalCount : 0

  return (
    <Command.Dialog
      open={open}
      onOpenChange={onOpenChange}
      label="Search tracks"
      shouldFilter={false}
      className="fixed inset-0 z-50 grid place-items-start justify-center pt-[12vh]"
    >
      <div
        className="fixed inset-0 bg-void/75 backdrop-blur-sm"
        onClick={() => onOpenChange(false)}
        aria-hidden="true"
      />

      <div className="relative w-[min(92vw,40rem)] overflow-hidden rounded-[var(--radius-lg)] border border-line-strong bg-surface-1 shadow-[var(--elev-3)]">
        <div className="flex items-center gap-3 border-b border-line px-4">
          {isSearching ? (
            <Loader2 className="size-4 shrink-0 animate-spin text-brand-bright" aria-hidden="true" />
          ) : (
            <Search className="size-4 shrink-0 text-text-faint" aria-hidden="true" />
          )}
          <Command.Input
            value={term}
            onValueChange={setTerm}
            autoFocus
            placeholder="Search 89,740 tracks by name or artist…"
            className="h-14 w-full bg-transparent text-[0.95rem] text-text outline-none placeholder:text-text-faint"
          />
          <kbd className="hidden shrink-0 rounded-md border border-line-strong bg-surface-2 px-1.5 py-0.5 font-mono text-[0.65rem] text-text-faint sm:block">
            ESC
          </kbd>
        </div>

        <Command.List className="max-h-[22rem] overflow-y-auto p-2">
          {!canSearch && (
            <p className="px-3 py-6 text-center text-sm text-text-faint">
              Type at least {MIN_QUERY_LENGTH} characters to search the catalog.
            </p>
          )}

          {canSearch && resource.state.status === 'failed' && (
            <p className="px-3 py-6 text-center text-sm text-danger">
              {resource.state.error.headline}
            </p>
          )}

          {canSearch && resource.state.status === 'ready' && items.length === 0 && (
            <Command.Empty className="px-3 py-6 text-center text-sm text-text-faint">
              No track matches “{debouncedTerm}”.
            </Command.Empty>
          )}

          {items.map((track) => (
            <Command.Item
              key={track.trackId}
              value={track.trackId}
              onSelect={() => {
                onOpenChange(false)
                navigate(`/catalog?track=${track.trackId}`)
              }}
              className="flex cursor-pointer items-center gap-3 rounded-[var(--radius-sm)] px-3 py-2.5 text-sm data-[selected=true]:bg-surface-3"
            >
              <TrackArt trackId={track.trackId} size={36} />
              <span className="min-w-0 flex-1">
                <span className="block truncate font-medium text-text">{track.name}</span>
                <span className="block truncate text-xs text-text-faint">{track.primaryArtist}</span>
              </span>
              <span className="shrink-0 font-mono text-xs text-text-faint">
                {formatDuration(track.durationMs)}
              </span>
              <span className="w-9 shrink-0 text-right font-mono text-xs text-brand-bright">
                {track.popularity}
              </span>
            </Command.Item>
          ))}

          {canSearch && resource.state.status === 'ready' && totalCount > items.length && (
            <p className="px-3 pt-3 pb-1 text-center text-xs text-text-faint">
              Showing {formatInteger(items.length)} of {formatInteger(totalCount)} matches
            </p>
          )}
        </Command.List>
      </div>
    </Command.Dialog>
  )
}
