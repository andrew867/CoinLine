import { useEffect, useState } from 'react'
import { apiGet } from '../api/client'

type Policy = {
  allowLiveFlashing: boolean
  liveDlaEnabled?: boolean
  transportMode?: 'simulation' | 'serial' | 'tcp' | 'pipe' | 'unknown'
  liveTransportConfigured?: boolean
  hardwareValidationNotice: string
}

export function FirmwareLiveFlashBanner() {
  const [pol, setPol] = useState<Policy | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    setErr(null)
    apiGet<Policy>('/api/firmware/execution-policy')
      .then(setPol)
      .catch(() => setErr('unavailable'))
  }, [])

  if (err) {
    return (
      <p role="status" style={{ color: '#888', fontSize: 14 }}>
        Firmware execution policy could not load ({err}).
      </p>
    )
  }

  if (!pol) {
    return (
      <p role="status" style={{ color: '#888' }}>
        Loading firmware execution policy…
      </p>
    )
  }

  const bothOn = pol.allowLiveFlashing && !!pol.liveDlaEnabled
  if (bothOn) {
    return (
      <div
        role="status"
        style={{
          background: '#f8fafc',
          border: '1px solid #cbd5e1',
          padding: '10px 12px',
          marginBottom: 16,
          borderRadius: 4,
          maxWidth: 960,
        }}
      >
        <strong>Live execution policy enabled</strong>
        <p style={{ margin: '6px 0 0', fontSize: 14, lineHeight: 1.4 }}>
          Both host switches are enabled.{' '}
          {pol.transportMode === 'simulation' ? 'Transport mode is simulation; no live endpoint is configured.' :
            pol.transportMode && pol.liveTransportConfigured === true ? `${pol.transportMode.toUpperCase()} transport settings are present.` :
            pol.transportMode && pol.liveTransportConfigured === false ? `${pol.transportMode.toUpperCase()} transport needs configuration.` :
            'Transport configuration is not reported by this server.'}
          {' '}Explicit job approval and terminal compatibility are separate from these settings.
        </p>
      </div>
    )
  }

  return (
    <div
      role="alert"
      style={{
        background: '#fdecea',
        border: '2px solid #c62828',
        padding: '10px 12px',
        marginBottom: 16,
        borderRadius: 4,
        maxWidth: 960,
      }}
    >
      <strong>Live firmware update disabled</strong>
      <p style={{ margin: '6px 0 0', fontSize: 14, lineHeight: 1.4 }}>
        Host runs simulation-only jobs by default. AllowLiveFlashing is{' '}
        {pol.allowLiveFlashing ? 'on' : 'off'}; live DLA is {pol.liveDlaEnabled ? 'on' : 'off'}. Modem flash remains{' '}
        <strong>HARDWARE_VALIDATION_REQUIRED</strong>. {pol.hardwareValidationNotice}
      </p>
    </div>
  )
}
