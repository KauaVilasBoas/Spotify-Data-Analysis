import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { AppShell } from '@/components/layout/AppShell'
import { NotFoundPage } from '@/pages/NotFoundPage'
import { OverviewPage } from '@/pages/OverviewPage'
import { UnderConstructionPage } from '@/pages/UnderConstructionPage'
import { CatalogSummaryProvider } from '@/providers/catalog-summary-provider'
import { navSections } from '@/navigation'

const plannedSections = navSections.filter((section) => section.status === 'em-construcao')

export function App() {
  return (
    <BrowserRouter>
      <CatalogSummaryProvider>
        <Routes>
          <Route element={<AppShell />}>
            <Route index element={<OverviewPage />} />
            {plannedSections.map((section) => (
              <Route key={section.path} path={section.path} element={<UnderConstructionPage />} />
            ))}
            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Routes>
      </CatalogSummaryProvider>
    </BrowserRouter>
  )
}
