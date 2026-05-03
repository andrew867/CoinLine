import { useEffect, useState } from 'react'
import { apiGet } from '../api/client'

type Policy = {
  allowLiveFlashing: boolean
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

  if (pol.allowLiveFlashing) {
    return (
      <div
        role="alert"
        style={{
          background: '#fff8e6',
          border: '2px solid #e65100',
          padding: '10px 12px',
          marginBottom: 16,
          borderRadius: 4,
          maxWidth: 960,
        }}
      >
        <strong>Live firmware flashing is enabled in configuration</strong>
        <p style={{ margin: '6px 0 0', fontSize: 14, lineHeight: 1.4 }}>
          This is unsafe until DLA/XMODEM transport is certified — {pol.hardwareValidationNotice}
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
        Host runs simulation-only jobs by default (<code>Firmware:AllowLiveFlashing</code> is false). Modem flash and
        XMODEM transfer remain <strong>HARDWARE_VALIDATION_REQUIRED</strong>. {pol.hardwareValidationNotice}
      </p>
    </div>
  )
}
