import type { EChartsCoreOption } from 'echarts/core'
import { useMemo } from 'react'
import type { AudioFeatureDistribution } from '@/api/insights'
import { formatInteger } from '@/lib/format'
import { CHART_COLORS, CHART_TEXT_STYLE, CHART_TOOLTIP } from './chart-theme'
import { EChart } from './EChart'

interface DistributionChartProps {
  distribution: AudioFeatureDistribution
  height?: number
}

export function DistributionChart({ distribution, height = 190 }: DistributionChartProps) {
  const option = useMemo<EChartsCoreOption>(() => {
    const labels = distribution.buckets.map((bucket) => bucket.lowerBound.toFixed(2))
    const values = distribution.buckets.map((bucket) => bucket.count)

    return {
      animationDuration: 720,
      animationEasing: 'cubicOut',
      grid: { top: 12, right: 6, bottom: 22, left: 6, containLabel: true },
      tooltip: {
        ...CHART_TOOLTIP,
        trigger: 'axis',
        axisPointer: { type: 'shadow' },
        formatter: (params: unknown) => {
          const rows = params as Array<{ dataIndex: number }>
          const first = rows[0]
          const bucket = first === undefined ? undefined : distribution.buckets[first.dataIndex]

          if (bucket === undefined) {
            return ''
          }

          return `${bucket.lowerBound.toFixed(2)} to ${bucket.upperBound.toFixed(2)}<br/><b>${formatInteger(bucket.count)}</b> tracks`
        },
      },
      xAxis: {
        type: 'category',
        data: labels,
        axisLine: { lineStyle: { color: CHART_COLORS.split } },
        axisTick: { show: false },
        axisLabel: { ...CHART_TEXT_STYLE, interval: Math.ceil(labels.length / 6) },
      },
      yAxis: {
        type: 'value',
        splitLine: { lineStyle: { color: CHART_COLORS.split } },
        axisLabel: { show: false },
      },
      series: [
        {
          type: 'bar',
          data: values,
          barCategoryGap: '18%',
          itemStyle: {
            borderRadius: [3, 3, 0, 0],
            color: {
              type: 'linear',
              x: 0,
              y: 0,
              x2: 0,
              y2: 1,
              colorStops: [
                { offset: 0, color: CHART_COLORS.brand },
                { offset: 1, color: CHART_COLORS.brandDim },
              ],
            },
          },
        },
      ],
    }
  }, [distribution])

  return (
    <EChart
      option={option}
      height={height}
      ariaLabel={`Distribution of ${distribution.feature} across the catalog`}
    />
  )
}
