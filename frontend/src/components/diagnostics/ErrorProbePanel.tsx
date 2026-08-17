import { useState } from 'react'
import { Radar } from 'lucide-react'
import { getTrackById } from '@/api/catalog'
import { toApiError, type ApiError } from '@/api/api-error'
import { Button } from '@/components/ui/button'
import { EmptyState } from '@/components/feedback/EmptyState'
import { ErrorState } from '@/components/feedback/ErrorState'
import { LoadingState } from '@/components/feedback/LoadingState'
import { Panel } from '@/components/foundation/Panel'

const MISSING_TRACK_ID = 'faixa-que-nao-existe'

type ProbeState =
  | { status: 'idle' }
  | { status: 'running' }
  | { status: 'unexpectedSuccess'; name: string }
  | { status: 'captured'; error: ApiError }

export function ErrorProbePanel() {
  const [probe, setProbe] = useState<ProbeState>({ status: 'idle' })

  async function runProbe() {
    setProbe({ status: 'running' })

    try {
      const track = await getTrackById(MISSING_TRACK_ID)
      setProbe({ status: 'unexpectedSuccess', name: track.name })
    } catch (error) {
      setProbe({ status: 'captured', error: toApiError(error) })
    }
  }

  const trigger = (
    <Button
      type="button"
      size="sm"
      variant="outline"
      onClick={() => void runProbe()}
      disabled={probe.status === 'running'}
      className="rounded-none border-hairline-strong bg-transparent font-mono text-xs tracking-wider uppercase hover:border-acid hover:bg-acid/10 hover:text-acid"
    >
      <Radar aria-hidden="true" />
      Disparar sonda
    </Button>
  )

  return (
    <Panel
      eyebrow="Sonda de erro · RFC 7807"
      aside={
        <span className="truncate font-mono text-[0.6875rem] text-bone-faint">
          GET /api/tracks/{MISSING_TRACK_ID}
        </span>
      }
    >
      {probe.status === 'idle' && (
        <EmptyState
          title="Nenhuma sonda executada"
          description="A sonda pede à API uma faixa que não existe. A resposta é um 404 em application/problem+json, e o cliente a converte na mesma mensagem legível que qualquer outra falha — sem tela branca e sem stack trace no usuário."
          hint="estado vazio compartilhado"
          action={trigger}
        />
      )}

      {probe.status === 'running' && <LoadingState label="Sondando o 404" rows={2} />}

      {probe.status === 'captured' && (
        <div className="space-y-4">
          <ErrorState error={probe.error} />
          {trigger}
        </div>
      )}

      {probe.status === 'unexpectedSuccess' && (
        <div className="space-y-4">
          <p className="text-sm text-bone-dim">
            A API encontrou uma faixa com esse identificador ({probe.name}). Troque o identificador da sonda.
          </p>
          {trigger}
        </div>
      )}
    </Panel>
  )
}
