import { useEffect, useState } from 'react'
import { apiGet } from '../api/client'

type SimState = {
  simulationMode: boolean
  physicalCardWritesDisabled: boolean
  banner?: string
}

export function SimulationBanner() {
  const [sim, setSim] = useState<SimState | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    setErr(null)
    apiGet<SimState>('/api/cards/simulation-state')
      .then(setSim)
      .catch(() => setErr('unavailable'))
  }, [])

  if (err) {
    return (
      <p role="status" style={{ color: '#888', fontSize: 14 }}>
        Card simulation banner could not load ({err}).
      </p>
    )
  }

  if (!sim) {
    return (
      <p role="status" style={{ color: '#888' }}>
        Loading card simulation state…
      </p>
    )
  }

  if (!sim.simulationMode) {
    return null
  }

  return (
    <div
      role="status"
      style={{
        background: '#fff3cd',
        border: '1px solid #ffc107',
        padding: '10px 12px',
        marginBottom: 16,
        borderRadius: 4,
        maxWidth: 960,
      }}
    >
      <strong>Card / payment simulation mode</strong>
      <p style={{ margin: '6px 0 0', fontSize: 14, lineHeight: 1.4 }}>
        {sim.banner ??
          'Ledger and reconciliation are lab scaffolding only. Treat balances as non-authoritative until UAT and HARDWARE_VALIDATION_REQUIRED.'}{' '}
        Physical card writes are {sim.physicalCardWritesDisabled ? 'blocked' : 'not guaranteed safe'}.
      </p>
    </div>
  )
}
