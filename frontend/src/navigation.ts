export type SectionStatus = 'disponivel' | 'em-construcao'

export interface NavSection {
  index: string
  path: string
  label: string
  summary: string
  status: SectionStatus
  card?: string
}

export const navSections: readonly NavSection[] = [
  {
    index: '01',
    path: '/',
    label: 'Visão geral',
    summary: 'Resumo do catálogo e cobertura de audio-features.',
    status: 'disponivel',
  },
  {
    index: '02',
    path: '/catalogo',
    label: 'Catálogo',
    summary: 'Busca e detalhe de faixas, artistas e álbuns.',
    status: 'em-construcao',
    card: 'E5.2',
  },
  {
    index: '03',
    path: '/insights',
    label: 'Insights',
    summary: 'Distribuições, correlações e recortes por gênero e ano de lançamento.',
    status: 'em-construcao',
    card: 'E5.3',
  },
  {
    index: '04',
    path: '/recomendacoes',
    label: 'Recomendações',
    summary: 'Faixas similares com explicabilidade por feature.',
    status: 'em-construcao',
  },
  {
    index: '05',
    path: '/predicao',
    label: 'Predição',
    summary: 'Estimativa de popularidade a partir das audio-features.',
    status: 'em-construcao',
  },
]

export function findSectionByPath(path: string): NavSection | undefined {
  return navSections.find((section) => section.path === path)
}
