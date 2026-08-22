export type SectionStatus = 'available' | 'planned'

export interface NavSection {
  path: string
  label: string
  summary: string
  status: SectionStatus
  card?: string
}

export const navSections: readonly NavSection[] = [
  {
    path: '/',
    label: 'Overview',
    summary: 'Catalog scale, audio-feature coverage and model standing.',
    status: 'available',
  },
  {
    path: '/catalog',
    label: 'Catalog',
    summary: 'Search and inspect tracks, artists and albums.',
    status: 'available',
  },
  {
    path: '/model',
    label: 'Model',
    summary: 'Popularity regression, feature importance and dataset splits.',
    status: 'planned',
    card: 'E5.3',
  },
]

export function findSectionByPath(path: string): NavSection | undefined {
  return navSections.find((section) => section.path === path)
}
