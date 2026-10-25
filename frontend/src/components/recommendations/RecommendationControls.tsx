import type { RecommendationParams } from '@/api/prediction'
import { cn } from '@/lib/utils'

// Apenas os três controles expostos ao usuário; blendWeight, explainTopK e dedupe
// ficam nos defaults do servidor (brief §2).

interface RecommendationControlsProps {
  params: Required<Pick<RecommendationParams, 'limit' | 'strategy' | 'genreMode'>>
  onChange: (next: Required<Pick<RecommendationParams, 'limit' | 'strategy' | 'genreMode'>>) => void
}

const STRATEGY_OPTIONS: { value: 'content' | 'blend'; label: string; hint: string }[] = [
  { value: 'content', label: 'Content', hint: 'Audio similarity only (cosine distance).' },
  { value: 'blend', label: 'Blend', hint: 'Mixes audio similarity with playlist co-occurrence.' },
]

const GENRE_OPTIONS: {
  value: 'boost' | 'off' | 'sameGenreOnly'
  label: string
  hint: string
}[] = [
  {
    value: 'boost',
    label: 'Boost',
    hint: 'Adds a +0.05 bonus to same-genre candidates, reordering without excluding.',
  },
  { value: 'off', label: 'Off', hint: 'Pure cosine audio similarity, no genre weighting.' },
  {
    value: 'sameGenreOnly',
    label: 'Same genre only',
    hint: 'Hard filter: only candidates that share the seed genre pass through.',
  },
]

/** Pill-group de seleção única reutilizável. */
function OptionGroup<T extends string>({
  label,
  options,
  value,
  onChange,
}: {
  label: string
  options: { value: T; label: string; hint: string }[]
  value: T
  onChange: (v: T) => void
}) {
  const selected = options.find((o) => o.value === value)

  return (
    <div className="space-y-2">
      <p className="label-micro">{label}</p>
      <div className="flex flex-wrap gap-1">
        {options.map((opt) => (
          <button
            key={opt.value}
            type="button"
            onClick={() => onChange(opt.value)}
            aria-pressed={opt.value === value}
            className={cn(
              'rounded-full px-3 py-1 text-xs font-medium transition-colors duration-150',
              opt.value === value
                ? 'bg-surface-3 text-text'
                : 'text-text-faint hover:bg-surface-2 hover:text-text-dim',
            )}
          >
            {opt.label}
          </button>
        ))}
      </div>
      {selected !== undefined && (
        <p className="text-xs text-text-faint">{selected.hint}</p>
      )}
    </div>
  )
}

export function RecommendationControls({ params, onChange }: RecommendationControlsProps) {
  return (
    <div className="flex flex-wrap gap-6">
      {/* Quantidade de resultados */}
      <div className="space-y-2">
        <label htmlFor="rec-limit" className="label-micro">
          Results
        </label>
        <div className="flex items-center gap-2">
          <input
            id="rec-limit"
            type="range"
            min={1}
            max={50}
            value={params.limit}
            onChange={(e) => onChange({ ...params, limit: Number(e.target.value) })}
            className="w-28 accent-brand-bright"
          />
          <span className="w-6 text-right font-mono text-xs text-text">{params.limit}</span>
        </div>
        <p className="text-xs text-text-faint">Number of recommendations returned (1–50).</p>
      </div>

      <OptionGroup
        label="Strategy"
        options={STRATEGY_OPTIONS}
        value={params.strategy}
        onChange={(v) => onChange({ ...params, strategy: v })}
      />

      <OptionGroup
        label="Genre mode"
        options={GENRE_OPTIONS}
        value={params.genreMode}
        onChange={(v) => onChange({ ...params, genreMode: v })}
      />
    </div>
  )
}
