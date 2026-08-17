import { NavLink } from 'react-router-dom'
import { navSections, type NavSection } from '@/navigation'
import { cn } from '@/lib/utils'

function SectionIndex({ value }: { value: string }) {
  return <span className="font-mono text-[0.625rem] tracking-widest text-bone-faint">{value}</span>
}

function AvailableSection({ section }: { section: NavSection }) {
  return (
    <NavLink
      to={section.path}
      end={section.path === '/'}
      className={({ isActive }) =>
        cn(
          'group relative flex items-baseline gap-3 border-l-2 py-2 pr-3 pl-4 transition-colors',
          isActive
            ? 'border-acid bg-acid/[0.07] text-bone'
            : 'border-transparent text-bone-dim hover:border-hairline-strong hover:text-bone',
        )
      }
    >
      <SectionIndex value={section.index} />
      <span className="display-wonk text-[1.0625rem] leading-none">{section.label}</span>
    </NavLink>
  )
}

function PlannedSection({ section }: { section: NavSection }) {
  return (
    <NavLink
      to={section.path}
      className={({ isActive }) =>
        cn(
          'group relative flex items-baseline gap-3 border-l-2 py-2 pr-3 pl-4 transition-colors',
          isActive
            ? 'border-bone-faint bg-bone/[0.04] text-bone-dim'
            : 'border-transparent text-bone-faint hover:text-bone-dim',
        )
      }
    >
      <SectionIndex value={section.index} />
      <span className="display-wonk text-[1.0625rem] leading-none">{section.label}</span>
      <span
        className="hatched ml-auto h-3 w-8 self-center border border-hairline-strong"
        aria-hidden="true"
      />
      <span className="sr-only">em construção</span>
    </NavLink>
  )
}

export function NavRail() {
  return (
    <nav aria-label="Seções do painel" className="flex flex-col gap-0.5">
      {navSections.map((section) =>
        section.status === 'disponivel' ? (
          <AvailableSection key={section.path} section={section} />
        ) : (
          <PlannedSection key={section.path} section={section} />
        ),
      )}
    </nav>
  )
}
