import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' } as const

type Cmd = {
  id: string
  craftSessionId: string
  commandName: string
  status: number
  auditReason: string
  destructiveConfirmed: boolean
  simulationExecution: boolean
  requestHex: string
  responseHex: string | null
  createdAtUtc: string
  updatedAtUtc: string
  commandTypeCode: string | null
}

function statusLabel(s: number) {
  const labels: Record<number, string> = {
    0: 'Queued',
    1: 'Sent',
    2: 'Succeeded',
    3: 'Failed',
    4: 'TimedOut',
    5: 'Running',
    6: 'Cancelled',
  }
  return labels[s] ?? String(s)
}

export function CraftCommandDetail() {
  const { commandId } = useParams()
  const [cmd, setCmd] = useState<Cmd | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    if (!commandId) return
    let c = false
    setLoading(true)
    setErr(null)
    ;(async () => {
      try {
        const r = await fetch(`/api/craft/commands/${commandId}`, { headers })
        if (r.status === 404) {
          if (!c) setErr('not_found')
          return
        }
        if (!r.ok) throw new Error(`${r.status}`)
        if (!c) setCmd((await r.json()) as Cmd)
      } catch (e) {
        if (!c) setErr(e instanceof Error ? e.message : 'error')
      } finally {
        if (!c) setLoading(false)
      }
    })()
    return () => {
      c = true
    }
  }, [commandId])

  if (!commandId) return <p role="status">Missing command id.</p>
  if (loading) return <p role="status">Loading command…</p>
  if (err === 'not_found')
    return (
      <div>
        <p>
          <Link to="/craft">← Craft sessions</Link>
        </p>
        <p role="alert">Command not found.</p>
      </div>
    )
  if (err)
    return (
      <div>
        <p>
          <Link to="/craft">← Craft sessions</Link>
        </p>
        <p role="alert" style={{ color: 'crimson' }}>
          {err}
        </p>
      </div>
    )
  if (!cmd)
    return (
      <div>
        <p>
          <Link to="/craft">← Craft sessions</Link>
        </p>
        <p role="status">No command data.</p>
      </div>
    )

  return (
    <div>
      <p>
        <Link to={`/craft/${cmd.craftSessionId}`}>← Craft session</Link>
      </p>
      <h1>Command: {cmd.commandName}</h1>
      <p>
        Status: <strong>{statusLabel(cmd.status)}</strong>
      </p>
      <p>Type: {cmd.commandTypeCode ?? '—'}</p>
      <p>Simulation execution: {cmd.simulationExecution ? 'yes' : 'no'}</p>
      <p>Destructive acknowledged: {cmd.destructiveConfirmed ? 'yes' : 'no'}</p>
      {cmd.auditReason ? <p>Audit reason: {cmd.auditReason}</p> : null}
      <p>
        Created {new Date(cmd.createdAtUtc).toLocaleString()} · Updated {new Date(cmd.updatedAtUtc).toLocaleString()}
      </p>
      <h2>Request / response (hex)</h2>
      <p style={{ color: '#555', maxWidth: 720 }}>
        Raw wire bytes are preserved server-side after hex decode; on-terminal framing remains HARDWARE_VALIDATION_REQUIRED.
      </p>
      <pre style={{ background: '#f6f8fa', padding: 12 }}>{cmd.requestHex}</pre>
      {cmd.responseHex ? (
        <pre style={{ background: '#f6f8fa', padding: 12 }}>{cmd.responseHex}</pre>
      ) : (
        <p role="status">No response bytes yet.</p>
      )}

      <h2>Status timeline</h2>
      <p style={{ color: '#555' }}>
        Commands move Queued → Running → Succeeded under host simulation unless defer/cancel. Live modem path is not enabled in this build (HARDWARE_VALIDATION_REQUIRED).
      </p>
      <ul>
        <li>Created: {new Date(cmd.createdAtUtc).toISOString()}</li>
        <li>Last update: {new Date(cmd.updatedAtUtc).toISOString()}</li>
      </ul>
    </div>
  )
}
