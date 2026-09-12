import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { AppShell } from '@/components/layout/AppShell'
import { CatalogPage } from '@/pages/CatalogPage'
import { InsightsPage } from '@/pages/InsightsPage'
import { ModelPage } from '@/pages/ModelPage'
import { NotFoundPage } from '@/pages/NotFoundPage'
import { OverviewPage } from '@/pages/OverviewPage'
import { RecommendationsPage } from '@/pages/RecommendationsPage'
import { UnderConstructionPage } from '@/pages/UnderConstructionPage'
import { navSections } from '@/navigation'

const plannedSections = navSections.filter((section) => section.status === 'planned')

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 5 * 60 * 1000,
      gcTime: 15 * 60 * 1000,
      retry: 1,
      refetchOnWindowFocus: false,
    },
  },
})

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <Routes>
          <Route element={<AppShell />}>
            <Route index element={<OverviewPage />} />
            <Route path="/catalog" element={<CatalogPage />} />
            <Route path="/insights" element={<InsightsPage />} />
            <Route path="/model" element={<ModelPage />} />
            <Route path="/recommendations" element={<RecommendationsPage />} />
            {plannedSections.map((section) => (
              <Route key={section.path} path={section.path} element={<UnderConstructionPage />} />
            ))}
            <Route path="*" element={<NotFoundPage />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </QueryClientProvider>
  )
}
