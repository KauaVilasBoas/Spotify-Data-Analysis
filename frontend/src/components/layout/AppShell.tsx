import { Outlet } from 'react-router-dom'
import { NavRail } from './NavRail'
import { ConnectionStrip } from './ConnectionStrip'

function Wordmark() {
  return (
    <div className="px-4 pt-7 pb-6">
      <p className="label-micro">catalog · analytics · prediction</p>
      <h1 className="display-wonk mt-2 text-[1.75rem] leading-[0.92] font-semibold text-bone">
        Spotify
        <br />
        <span className="text-acid">Data</span> Analysis
      </h1>
      <div className="mt-4 h-px w-10 bg-acid" />
    </div>
  )
}

function RailFooter() {
  return (
    <div className="mt-auto space-y-1 px-4 py-6">
      <p className="label-micro">monolito modular · .net 8</p>
      <p className="label-micro">ddd · cqrs · outbox</p>
    </div>
  )
}

export function AppShell() {
  return (
    <div className="surface-grain min-h-dvh bg-ink">
      <div className="mx-auto flex min-h-dvh w-full max-w-[110rem] flex-col lg:flex-row">
        <aside className="flex shrink-0 flex-col border-b border-hairline bg-ink-sunken/60 lg:h-dvh lg:w-[16.5rem] lg:sticky lg:top-0 lg:border-r lg:border-b-0">
          <Wordmark />
          <NavRail />
          <RailFooter />
        </aside>

        <div className="flex min-w-0 flex-1 flex-col">
          <ConnectionStrip />
          <main className="flex-1 px-5 py-8 md:px-8 lg:py-12">
            <Outlet />
          </main>
        </div>
      </div>
    </div>
  )
}
