import { useEffect, useState } from 'react'
import { apiGet } from '../api/client'

type State = {
  runtimeAuthorizationProviderRegistered?: boolean
  labAuthorizationEnabled?: boolean
  labSettlementEnabled?: boolean
  shadyBankAuthorizationEnabled?: boolean
  shadyBankRouteBindingCount?: number
  shadyBankCaptureEnabled?: boolean
  physicalCardWritesDisabled: boolean
}

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
          registered === true && state.shadyBankAuthorizationEnabled ?
            'ShadyBank authorization configured. Only explicitly bound test instruments and reserved routes can be approved. Current bank connectivity is not established by this status.' :
          registered === true && state.labAuthorizationEnabled ?
            'Synthetic lab authorization configured. Only explicitly bound test accounts and local test routes can be approved.' :
          registered === true ? 'Authorization provider registered. Processor connectivity and settlement readiness are not established by this status.' :
          'This host does not report its runtime authorization provider status.'}
      </p>
      <p style={{ margin: '6px 0 0', color: '#475569', fontSize: 14 }}>
        Card accounts, terminal acceptance and tariff tables are separate from runtime authorization.
        {state?.labSettlementEnabled && ' Synthetic CDR ledger posting is enabled for this isolated lab; payment reconciliation remains separate.'}
        {state?.physicalCardWritesDisabled && ' Physical card writes are disabled.'}
      </p>
      {registered === true && state?.shadyBankAuthorizationEnabled && (
        <dl style={{ display: 'grid', gridTemplateColumns: 'max-content 1fr', gap: '4px 12px', margin: '10px 0 0', fontSize: 14 }} aria-label="ShadyBank configuration">
          <dt>Payment route scope</dt>
          <dd style={{ margin: 0 }}>{typeof state.shadyBankRouteBindingCount === 'number' ? `${state.shadyBankRouteBindingCount} configured terminal/route binding${state.shadyBankRouteBindingCount === 1 ? '' : 's'}` : 'Not reported by this host'}</dd>
          <dt>Uploaded CDR capture</dt>
          <dd style={{ margin: 0 }}>{state.shadyBankCaptureEnabled === true ? 'Enabled for explicitly qualified bindings. Authorization and capture remain separate.' : state.shadyBankCaptureEnabled === false ? 'Disabled. An approved authorization does not post a purchase.' : 'Not reported by this host'}</dd>
        </dl>
      )}
    </section>
  )
}
