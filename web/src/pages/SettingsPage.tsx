import type { FormEvent } from 'react'
import { useEffect, useState } from 'react'
import { ErrorBanner } from '../components/AppStates'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'

const headers = { 'Content-Type': 'application/json' }

type Me = { username: string; displayName: string; role: string }

type CraftPolicy = {
  allowLiveCraft?: boolean
  liveEnabled?: boolean
  activeNccLinks?: number
}

type FirmwarePolicy = {
  allowLiveFlashing?: boolean
  liveDlaEnabled?: boolean
}

type SerialModem = {
  enabled?: boolean
  watching?: boolean
  phase?: string
  portName?: string | null
  note?: string
}

export function SettingsPage() {
  const [me, setMe] = useState<Me | null>(null)
  const [currentPassword, setCurrentPassword] = useState('')
  const [newPassword, setNewPassword] = useState('')
  const [apiKey, setApiKey] = useState(() => consoleSession.apiKey() ?? '')
  const [err, setErr] = useState<string | null>(null)
  const [note, setNote] = useState<string | null>(null)
  const [craft, setCraft] = useState<CraftPolicy | null>(null)
  const [firmware, setFirmware] = useState<FirmwarePolicy | null>(null)
  const [serial, setSerial] = useState<SerialModem | null>(null)

  useEffect(() => {
    void apiFetch('/api/auth/me', { headers })
      .then((r) => (r.ok ? (r.json() as Promise<Me>) : null))
      .then((body) => {
        if (body) setMe(body)
      })
      .catch(() => {})
    void apiFetch('/api/craft/execution-policy', { headers })
      .then((r) => (r.ok ? (r.json() as Promise<CraftPolicy>) : null))
      .then((body) => {
        if (body) setCraft(body)
      })
      .catch(() => {})
    void apiFetch('/api/firmware/execution-policy', { headers })
      .then((r) => (r.ok ? (r.json() as Promise<FirmwarePolicy>) : null))
      .then((body) => {
        if (body) setFirmware(body)
      })
      .catch(() => {})
    void apiFetch('/api/serial-modem', { headers })
      .then((r) => (r.ok ? (r.json() as Promise<SerialModem>) : null))
      .then((body) => {
        if (body) setSerial(body)
      })
      .catch(() => {})
  }, [])

  async function onPassword(e: FormEvent) {
    e.preventDefault()
    setErr(null)
    setNote(null)
    const r = await apiFetch('/api/auth/password', {
      method: 'POST',
      headers,
      body: JSON.stringify({ currentPassword, newPassword }),
    })
    if (!r.ok) {
      const body = (await r.json().catch(() => null)) as { error?: string } | null
      setErr(body?.error ?? 'Could not change the password')
      return
    }
    setCurrentPassword('')
    setNewPassword('')
    consoleSession.clear()
    window.location.assign('/login')
  }

  function onKey(e: FormEvent) {
    e.preventDefault()
    const value = apiKey.trim()
    consoleSession.setApiKey(value)
    setNote(value ? 'API key saved in this browser.' : 'API key cleared.')
  }

  return (
    <div>
      <h1>Settings</h1>
      {me && (
        <p className="page-intro">
          Signed in as {me.displayName} ({me.username}), role {me.role}.
        </p>
      )}
      {err && <ErrorBanner message={err} />}
      {note && <p>{note}</p>}

      <h2>Live gates</h2>
      <p className="page-intro">
        Default Compose leaves these off. Turning them on is an operator choice after certification.
      </p>
      <dl style={{ display: 'grid', gridTemplateColumns: '220px 1fr', gap: 8, maxWidth: 720 }}>
        <dt>Craft:AllowLiveCraft</dt>
        <dd>{craft ? (craft.allowLiveCraft ? 'on' : 'off') : '…'}</dd>
        <dt>COINLINE_CRAFT_LIVE_ENABLED</dt>
        <dd>{craft ? (craft.liveEnabled ? 'on' : 'off') : '…'}</dd>
        <dt>Active NCC links</dt>
        <dd>{craft?.activeNccLinks ?? '…'}</dd>
        <dt>Firmware:AllowLiveFlashing</dt>
        <dd>{firmware ? (firmware.allowLiveFlashing ? 'on' : 'off') : '…'}</dd>
        <dt>COINLINE_FIRMWARE_LIVE_DLA_ENABLED</dt>
        <dd>{firmware ? (firmware.liveDlaEnabled ? 'on' : 'off') : '…'}</dd>
        <dt>Serial modem inlet</dt>
        <dd>
          {serial
            ? serial.enabled || serial.watching
              ? `${serial.phase ?? 'on'}${serial.portName ? ` (${serial.portName})` : ''}`
              : 'off'
            : '…'}
        </dd>
      </dl>

      <h2>Password</h2>
      <p className="page-intro">Changing your password ends your current sign-in sessions.</p>
      <form onSubmit={(e) => void onPassword(e)} className="row-actions">
        <label>
          Current password
          <input
            type="password"
            value={currentPassword}
            onChange={(e) => setCurrentPassword(e.target.value)}
            autoComplete="current-password"
            required
          />
        </label>
        <label>
          New password
          <input
            type="password"
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            autoComplete="new-password"
            minLength={8}
            required
          />
        </label>
        <button type="submit">Change password</button>
      </form>
      <h2>API key</h2>
      <p className="page-intro">Leave this blank in the lab. Paste a key when the server requires one.</p>
      <form onSubmit={onKey} className="row-actions">
        <label>
          Key
          <input value={apiKey} onChange={(e) => setApiKey(e.target.value)} autoComplete="off" />
        </label>
        <button type="submit">Save key</button>
      </form>
    </div>
  )
}
