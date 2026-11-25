import { useEffect, useState } from 'react'
import { apiGet } from '../api/client'

type State = { runtimeAuthorizationProviderRegistered?: boolean; physicalCardWritesDisabled: boolean }

export function RuntimeCardAuthorizationStatus() {
  const [state, setState] = useState<State | null>(null)
  const [unavailable, setUnavailable] = useState(false)

  useEffect(() => {
    apiGet<State>('/api/cards/simulation-state').then(setState).catch(() => setUnavailable(true))
  }, [])

  const registered = state?.runtimeAuthorizationProviderRegistered
  return (
    <section aria-label="Runtime card authorization" style={{ background: '#f8fafc', border: '1px solid #cbd5e1',
      borderRadius: 6, padding: '12px 16px', marginBottom: 16 }}>
      <h2 style={{ margin: '0 0 6px', fontSize: 17 }}>Runtime card authorization</h2>
      <p role="status" style={{ margin: 0 }}>
        {unavailable ? 'Authorization status could not be loaded.' : !state ? 'Loading authorization status...' :
          registered === false ? 'No authorization provider registered. Terminal card authorizations will be declined.' :
          registered === true ? 'Authorization provider registered. Processor connectivity and settlement readiness are not established by this status.' :
          'This host does not report its runtime authorization provider status.'}
      </p>
      <p style={{ margin: '6px 0 0', color: '#475569', fontSize: 14 }}>
        Card accounts and reader tests are separate from runtime authorization.
        {state?.physicalCardWritesDisabled && ' Physical card writes are disabled.'}
      </p>
    </section>
  )
}
