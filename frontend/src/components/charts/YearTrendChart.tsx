import type { EChartsCoreOption } from 'echarts/core'
import { useMemo } from 'react'
import type { AlbumYearInsightItem } from '@/api/insights'
import { formatDecimal, formatInteger } from '@/lib/format'
import { CHART_COLORS, CHART_TEXT_STYLE, CHART_TOOLTIP } from './chart-theme'
import { EChart } from './EChart'

interface YearTrendChartProps {
  /** Only rows with a known year. The null-year bucket is surfaced as text by the panel, not here. */
  points: AlbumYearInsightItem[]
  height?: number
}

/**
 * Average popularity per release year as a line. Only rows with a known year are plotted; the
 * null-year bucket cannot sit on a temporal axis, so the panel reports it separately instead of
 * hiding it. Correlation-free by design: it is a descriptive trend, not a causal claim.
 */
export function YearTrendChart({ points, height = 260 }: YearTrendChartProps) {
  const option = useMemo<EChartsCoreOption>(() => {
    const ordered = [...points]
      .filter((point) => point.year !== null)
      .sort((a, b) => (a.year ?? 0) - (b.year ?? 0))

    const labels = ordered.map((point) => String(point.year))
    const values = ordered.map((point) => Number(point.averagePopularity.toFixed(2)))

    return {
      animationDuration: 720,
      animationEasing: 'cubicOut',
      grid: { top: 16, right: 12, bottom: 24, left: 6, containLabel: true },
      tooltip: {
        ...CHART_TOOLTIP,
        trigger: 'axis',
        formatter: (params: unknown) => {
          const rows = params as Array<{ dataIndex: number }>
          const first = rows[0]
          const point = first === undefined ? undefined : ordered[first.dataIndex]

          if (point === undefined) {
            return ''
          }

          return `${point.year}<br/>avg popularity <b>${formatDecimal(point.averagePopularity, 1)}</b><br/>${formatInteger(point.trackCount)} tracks`
        },
      },
      xAxis: {
        type: 'category',
        data: labels,
        boundaryGap: false,
        axisLine: { lineStyle: { color: CHART_COLORS.split } },
        axisTick: { show: false },
        axisLabel: { ...CHART_TEXT_STYLE, interval: Math.ceil(Math.max(labels.length, 1) / 8) },
      },
      yAxis: {
        type: 'value',
        splitLine: { lineStyle: { color: CHART_COLORS.split } },
        axisLabel: { ...CHART_TEXT_STYLE },
      },
      series: [
        {
          type: 'line',
          data: values,
          smooth: true,
          showSymbol: false,
          lineStyle: { color: CHART_COLORS.brand, width: 2 },
          areaStyle: {
            color: {
              type: 'linear',
              x: 0,
              y: 0,
              x2: 0,
              y2: 1,
              colorStops: [
                { offset: 0, color: 'rgba(30, 215, 96, 0.28)' },
                { offset: 1, color: 'rgba(30, 215, 96, 0)' },
              ],
            },
          },
        },
      ],
    }
  }, [points])

  return (
    <EChart
      option={option}
      height={height}
      ariaLabel="Average track popularity by release year across the catalog"
    />
  )
}
