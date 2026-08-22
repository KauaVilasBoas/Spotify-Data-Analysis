import type { EChartsCoreOption } from 'echarts/core'
import { useMemo } from 'react'
import type { FeatureCorrelation } from '@/api/insights'
import { formatSignedDecimal } from '@/lib/format'
import { CHART_COLORS, CHART_TEXT_STYLE, CHART_TOOLTIP } from './chart-theme'
import { EChart } from './EChart'

interface CorrelationChartProps {
  correlations: FeatureCorrelation[]
  height?: number
}

export function CorrelationChart({ correlations, height = 280 }: CorrelationChartProps) {
  const option = useMemo<EChartsCoreOption>(() => {
    // A null coefficient means PostgreSQL could not compute `corr()`; it is dropped from the plot
    // (never rendered as zero). The Insights screen surfaces those cases explicitly elsewhere.
    const computed = correlations.filter(
      (item): item is FeatureCorrelation & { coefficient: number } => item.coefficient !== null,
    )
    const sorted = [...computed].sort((a, b) => a.coefficient - b.coefficient)

    return {
      animationDuration: 720,
      animationEasing: 'cubicOut',
      grid: { top: 8, right: 52, bottom: 24, left: 6, containLabel: true },
      tooltip: {
        ...CHART_TOOLTIP,
        trigger: 'item',
        formatter: (params: unknown) => {
          const row = params as { dataIndex: number }
          const item = sorted[row.dataIndex]

          if (item === undefined) {
            return ''
          }

          return `${item.feature}<br/>Pearson r <b>${formatSignedDecimal(item.coefficient)}</b>`
        },
      },
      xAxis: {
        type: 'value',
        max: 0.15,
        min: -0.15,
        splitLine: { lineStyle: { color: CHART_COLORS.split } },
        axisLabel: { ...CHART_TEXT_STYLE },
      },
      yAxis: {
        type: 'category',
        data: sorted.map((item) => item.feature),
        axisLine: { show: false },
        axisTick: { show: false },
        axisLabel: { ...CHART_TEXT_STYLE },
      },
      series: [
        {
          type: 'bar',
          data: sorted.map((item) => ({
            value: item.coefficient,
            itemStyle: {
              color: item.coefficient >= 0 ? CHART_COLORS.positive : CHART_COLORS.negative,
              borderRadius: 3,
            },
          })),
          barWidth: '52%',
          label: {
            show: true,
            position: 'right',
            formatter: (params: unknown) => formatSignedDecimal((params as { value: number }).value),
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
      ariaLabel="Pearson correlation between each audio feature and track popularity"
    />
  )
}
