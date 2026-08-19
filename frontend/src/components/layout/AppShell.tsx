import { useCallback, useEffect, useState } from 'react'
import { Outlet } from 'react-router-dom'
import { CommandPalette } from '@/components/command/CommandPalette'
import { SiteFooter } from './SiteFooter'
import { TopNav } from './TopNav'

export function AppShell() {
  const [paletteOpen, setPaletteOpen] = useState(false)

  const openPalette = useCallback(() => setPaletteOpen(true), [])

  useEffect(() => {
    function handleShortcut(event: KeyboardEvent) {
      if (event.key.toLowerCase() === 'k' && (event.metaKey || event.ctrlKey)) {
        event.preventDefault()
        setPaletteOpen((previous) => !previous)
      }
    }

    document.addEventListener('keydown', handleShortcut)
    return () => document.removeEventListener('keydown', handleShortcut)
  }, [])

  return (
    <div className="relative min-h-dvh bg-void">
      <div className="aurora fixed h-[70vh]" aria-hidden="true" />
      <div className="grain fixed" aria-hidden="true" />

      <div className="relative flex min-h-dvh flex-col">
        <TopNav onOpenCommandPalette={openPalette} />

        <main className="flex-1 px-6 pt-10 pb-4 md:px-10">
          <div className="mx-auto w-full max-w-[100rem]">
            <Outlet />
          </div>
        </main>

        <SiteFooter />
      </div>

      <CommandPalette open={paletteOpen} onOpenChange={setPaletteOpen} />
    </div>
  )
}
