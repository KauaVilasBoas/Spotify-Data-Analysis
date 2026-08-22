import type { EChartsCoreOption } from 'echarts/core'
import { useMemo } from 'react'
import type { FeatureCorrelation } from '@/api/insights'
import { formatInteger, formatSignedDecimal } from '@/lib/format'
import { CHART_COLORS, CHART_TEXT_STYLE, CHART_TOOLTIP } from './chart-theme'
import { EChart } from './EChart'

interface FeatureCorrelationChartProps {
  correlations: FeatureCorrelation[]
  height?: number
}

/** Lowercases the PascalCase feature name the API echoes ("Danceability" -> "danceability"). */
function toDisplayFeature(feature: string): string {
  return feature.toLowerCase()
}

/**
 * Pearson coefficient of every continuous audio feature against popularity, as a diverging bar
 * chart. Features whose coefficient came back null (not computable) are kept on the axis but drawn
 * with no bar and flagged in the tooltip, so a missing coefficient never reads as a zero. The
 * value axis is symmetric around zero and sized to the largest magnitude present.
 */
export function FeatureCorrelationChart({ correlations, height = 320 }: FeatureCorrelationChartProps) {
  const option = useMemo<EChartsCoreOption>(() => {
    const sorted = [...correlations].sort((a, b) => (a.coefficient ?? 0) - (b.coefficient ?? 0))

    const magnitudes = sorted
      .map((item) => (item.coefficient === null ? 0 : Math.abs(item.coefficient)))
      .filter((value) => value > 0)
    const peak = magnitudes.length > 0 ? Math.max(...magnitudes) : 0.1
    const axisBound = Math.max(0.05, Math.ceil(peak * 100 + 1) / 100)

    return {
      animationDuration: 720,
      animationEasing: 'cubicOut',
      grid: { top: 8, right: 64, bottom: 24, left: 6, containLabel: true },
      tooltip: {
        ...CHART_TOOLTIP,
        trigger: 'item',
        formatter: (params: unknown) => {
          const row = params as { dataIndex: number }
          const item = sorted[row.dataIndex]

          if (item === undefined) {
            return ''
          }

          const value =
            item.coefficient === null
              ? '<span style="color:#8a93a3">not computable</span>'
              : `<b>${formatSignedDecimal(item.coefficient)}</b>`

          return `${toDisplayFeature(item.feature)}<br/>Pearson r ${value}<br/>n = ${formatInteger(item.n)}`
        },
      },
      xAxis: {
        type: 'value',
        max: axisBound,
        min: -axisBound,
        splitLine: { lineStyle: { color: CHART_COLORS.split } },
        axisLabel: { ...CHART_TEXT_STYLE },
      },
      yAxis: {
        type: 'category',
        data: sorted.map((item) => toDisplayFeature(item.feature)),
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { ...CHART_TEXT_STYLE },
      },
      series: [
        {
          type: 'bar',
          data: sorted.map((item) => ({
            value: item.coefficient ?? 0,
            itemStyle: {
              color:
                item.coefficient === null
                  ? 'transparent'
                  : item.coefficient >= 0
                    ? CHART_COLORS.positive
                    : CHART_COLORS.negative,
              borderRadius: 3,
            },
          })),
          barWidth: '54%',
          label: {
            show: true,
            position: 'right',
            formatter: (params: unknown) => {
              const item = sorted[(params as { dataIndex: number }).dataIndex]

              if (item === undefined || item.coefficient === null) {
                return 'n/a'
              }

              return formatSignedDecimal(item.coefficient)
            },
            ...CHART_TEXT_STYLE,
          },
        },
      ],
    }
  }, [correlations])

  return (
    <EChart
      option={option}
      height={height}
      ariaLabel="Pearson correlation between each continuous audio feature and track popularity, with the sample size per feature"
    />
  )
}
