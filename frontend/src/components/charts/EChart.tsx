import { BarChart } from 'echarts/charts'
import { GridComponent, TooltipComponent } from 'echarts/components'
import { init, use, type ECharts, type EChartsCoreOption } from 'echarts/core'
import { CanvasRenderer } from 'echarts/renderers'
import { useEffect, useRef } from 'react'
import { cn } from '@/lib/utils'

use([BarChart, GridComponent, TooltipComponent, CanvasRenderer])

interface EChartProps {
  option: EChartsCoreOption
  height: number
  className?: string
  ariaLabel: string
}

/**
 * Thin ECharts host that registers only the chart types and components this application draws,
 * so the tree-shaken bundle never pulls the full `echarts` barrel.
 */
export function EChart({ option, height, className, ariaLabel }: EChartProps) {
  const containerRef = useRef<HTMLDivElement>(null)
  const chartRef = useRef<ECharts | null>(null)

  useEffect(() => {
    const container = containerRef.current

    if (container === null) {
      return
    }

    const chart = init(container, undefined, { renderer: 'canvas' })
    chartRef.current = chart

    const observer = new ResizeObserver(() => chart.resize())
    observer.observe(container)

    return () => {
      observer.disconnect()
      chart.dispose()
      chartRef.current = null
    }
  }, [])

  useEffect(() => {
    chartRef.current?.setOption(option, true)
  }, [option])

  return (
    <div
      ref={containerRef}
      role="img"
      aria-label={ariaLabel}
      style={{ height }}
      className={cn('w-full', className)}
    />
  )
}
