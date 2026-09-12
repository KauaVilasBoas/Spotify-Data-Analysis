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
    path: '/insights',
    label: 'Insights',
    summary: 'Feature-vs-popularity correlation, distributions and genre and year cuts.',
    status: 'available',
    card: 'E5.3',
  },
  {
    path: '/model',
    label: 'Model',
    summary: 'Popularity regression, feature importance and dataset splits.',
    status: 'available',
  },
  {
    path: '/recommendations',
    label: 'Recommendations',
    summary: 'Find tracks similar to a seed using content-based and collaborative signals.',
    status: 'available',
  },
]

export function findSectionByPath(path: string): NavSection | undefined {
  return navSections.find((section) => section.path === path)
}
