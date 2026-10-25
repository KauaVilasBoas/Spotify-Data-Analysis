import { NavLink } from 'react-router-dom'
import { SpotifyMark } from '@/components/brand/SpotifyMark'
import { navSections } from '@/navigation'
import { cn } from '@/lib/utils'

interface TopNavProps {
  onOpenCommandPalette: () => void
}

export function TopNav({ onOpenCommandPalette }: TopNavProps) {
  return (
    <header className="sticky top-0 z-40 border-b border-line/60 bg-void/80 backdrop-blur-xl">
      <div className="mx-auto flex h-16 max-w-[100rem] items-center gap-6 px-6 md:px-10">
        <NavLink to="/" className="flex items-center gap-2.5" aria-label="Spotify Data Analysis, home">
          <SpotifyMark size={26} title="Spotify logo" />
          <span className="hidden text-[0.94rem] font-semibold tracking-tight text-text sm:inline">
            Data Analysis
          </span>
        </NavLink>

        <nav aria-label="Primary" className="flex items-center gap-1">
          {navSections.map((section) => (
            <NavLink
              key={section.path}
              to={section.path}
              end={section.path === '/'}
              className={({ isActive }) =>
                cn(
                  'relative rounded-full px-3.5 py-1.5 text-[0.86rem] font-medium transition-colors duration-200',
                  isActive
                    ? 'bg-surface-2 text-text'
                    : 'text-text-faint hover:bg-surface-1 hover:text-text-dim',
                )
              }
            >
              {section.label}
              {section.status === 'planned' && (
                <>{' '}<span className="ml-1.5 align-middle text-[0.6rem] text-text-faint">soon</span></>
              )}
            </NavLink>
          ))}
        </nav>

        <button
          type="button"
          onClick={onOpenCommandPalette}
          className="ml-auto flex items-center gap-2.5 rounded-full border border-line-strong bg-surface-1/70 py-1.5 pr-2 pl-3.5 text-text-faint transition-colors duration-200 hover:border-brand/50 hover:text-text-dim"
          aria-label="Open track search. Keyboard shortcut: control or command plus K"
        >
          <span className="text-[0.82rem]">Search tracks</span>
          <kbd className="rounded-md border border-line-strong bg-surface-2 px-1.5 py-0.5 font-mono text-[0.65rem] text-text-faint">
            ⌘K
          </kbd>
        </button>
      </div>
    </header>
  )
}
