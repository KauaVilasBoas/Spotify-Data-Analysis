import { AlertTriangle } from 'lucide-react'
import type { TrackRecommendations } from '@/api/prediction'
import { formatInteger } from '@/lib/format'
import { cn } from '@/lib/utils'

// Normaliza a caixa do enum .NET ("Content" → "content", "Boost" → "boost")
// sem quebrar a comparação posterior.
function normalizeEnum(value: string): string {
  return value.charAt(0).toLowerCase() + value.slice(1)
}

// Converte nome interno do enum para label legível.
function strategyLabel(raw: string): string {
  const normalized = normalizeEnum(raw)
  if (normalized === 'blend') return 'Blend'
  if (normalized === 'content') return 'Content'
  return raw
}

function genreModeLabel(raw: string): string {
  const normalized = normalizeEnum(raw)
  if (normalized === 'boost') return 'Boost'
  if (normalized === 'off') return 'Off'
  if (normalized === 'sameGenreOnly') return 'Same genre only'
  return raw
}

interface HonestyProps {
  rec: TrackRecommendations
  requestedStrategy: 'content' | 'blend'
  // requestedGenreMode vem do echo da API (rec.requestedGenreMode), não de prop,
  // para que uma requisição ainda em voo não afirme o modo de uma resposta anterior.
}

interface AlertRowProps {
  children: React.ReactNode
  severity?: 'warn' | 'info'
}

function AlertRow({ children, severity = 'warn' }: AlertRowProps) {
  return (
    <div
      className={cn(
        'flex items-start gap-2.5 rounded-[var(--radius-sm)] border px-3.5 py-3 text-sm',
        severity === 'warn'
          ? 'border-imputed/40 bg-imputed/[0.07] text-text-dim'
          : 'border-line-strong bg-surface-1 text-text-dim',
      )}
    >
      <AlertTriangle
        className={cn(
          'mt-px size-4 shrink-0',
          severity === 'warn' ? 'text-imputed' : 'text-text-faint',
        )}
        aria-hidden="true"
      />
      <span>{children}</span>
    </div>
  )
}

/**
 * Painel de honestidade: exibe warnings, fallbacks e divergências entre o que
 * foi pedido e o que a API efetivamente aplicou. É o coração da tela.
 */
export function RecommendationHonesty({ rec, requestedStrategy }: HonestyProps) {
  const effectiveStrategyNorm = normalizeEnum(rec.effectiveStrategy)
  const effectiveGenreModeNorm = normalizeEnum(rec.effectiveGenreMode)
  // requestedGenreMode: lido do echo da API porque não há campo equivalente para
  // estratégia — a API só devolve effectiveStrategy, não requestedStrategy.
  // Estratégia continua comparada via prop para não introduzir assimetria falsa.
  const requestedGenreMode = normalizeEnum(rec.requestedGenreMode)

  const strategyDiverged = effectiveStrategyNorm !== requestedStrategy
  const genreModeDiverged = effectiveGenreModeNorm !== requestedGenreMode

  const hasAnything =
    rec.warnings.length > 0 ||
    rec.collaborativeSignalUnavailable ||
    strategyDiverged ||
    genreModeDiverged ||
    rec.genreFellBackToCosineOnly ||
    rec.seedIsImputed

  return (
    <div className="space-y-4">
      {/* Warnings do servidor — sempre visíveis, sem "ver mais" */}
      {rec.warnings.map((w, i) => (
        <AlertRow key={i} severity="warn">
          {w}
        </AlertRow>
      ))}

      {/* Sinal colaborativo ausente (blend pedido, content aplicado) */}
      {rec.collaborativeSignalUnavailable && (
        <AlertRow severity="warn">
          <strong>Collaborative signal unavailable.</strong> The seed track has no playlist
          co-occurrence data recorded. The blend strategy fell back to content-only similarity.
        </AlertRow>
      )}

      {/* Estratégia divergiu por outro motivo que não o sinal colaborativo */}
      {strategyDiverged && !rec.collaborativeSignalUnavailable && (
        <AlertRow severity="warn">
          <strong>Strategy fallback.</strong> Requested{' '}
          <strong>{strategyLabel(requestedStrategy)}</strong>; effective strategy was{' '}
          <strong>{strategyLabel(rec.effectiveStrategy)}</strong>.
        </AlertRow>
      )}

      {/* Modo de gênero divergiu */}
      {genreModeDiverged && (
        <AlertRow severity="warn">
          <strong>Genre mode fallback.</strong> Requested{' '}
          <strong>{genreModeLabel(requestedGenreMode)}</strong>; effective mode was{' '}
          <strong>{genreModeLabel(rec.effectiveGenreMode)}</strong>.
        </AlertRow>
      )}

      {/* Semente sem gênero utilizável */}
      {rec.genreFellBackToCosineOnly && (
        <AlertRow severity="warn">
          <strong>Genre unavailable for seed.</strong> The seed track carries no usable genre;
          genre mode was ignored and pure cosine similarity was applied.
        </AlertRow>
      )}

      {/* Semente imputada */}
      {rec.seedIsImputed && (
        <AlertRow severity="info">
          <strong>Seed is imputed.</strong> The seed track&rsquo;s audio features were estimated
          from the genre median, not measured from the audio. Recommendations are based on
          estimated values.
        </AlertRow>
      )}

      {/* Estatísticas do índice — sempre presentes */}
      <div className="flex flex-wrap gap-x-6 gap-y-1 text-xs text-text-faint">
        <span>
          Index size: <span className="text-text-dim">{formatInteger(rec.indexedTrackCount)} tracks</span>
        </span>
        <span>
          Dedupe:{' '}
          <span className="text-text-dim">
            {rec.dedupeApplied
              ? `${formatInteger(rec.totalDuplicatesCollapsed)} duplicates collapsed`
              : 'off'}
          </span>
        </span>
        <span>
          Effective strategy:{' '}
          <span className="text-text-dim">{strategyLabel(rec.effectiveStrategy)}</span>
        </span>
        <span>
          Effective genre mode:{' '}
          <span className="text-text-dim">{genreModeLabel(rec.effectiveGenreMode)}</span>
        </span>
      </div>

      {/* Se não há nenhum aviso, confirma que tudo foi como pedido */}
      {!hasAnything && (
        <p className="text-xs text-text-faint">
          All parameters applied as requested. No fallbacks.
        </p>
      )}
    </div>
  )
}
