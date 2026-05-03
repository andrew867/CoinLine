import { useState } from 'react'
import { Link } from 'react-router-dom'

const headers = {
  'Content-Type': 'application/json',
  'X-Operator-Id': 'ui@local',
  'X-Operator-Role': 'Admin',
}

type ReplayResult = {
  transactions: { id: string; messageType: number; capturedAtUtc: string; rawPayloadHex: string }[]
  concatenatedPayloadHex: string
  totalByteLength: number
}

export function DlogReplayDebug() {
  const [terminalId, setTerminalId] = useState('')
  const [messageType, setMessageType] = useState('')
  const [direction, setDirection] = useState('')
  const [processingStatus, setProcessingStatus] = useState('')
  const [sessionCorrelationId, setSessionCorrelationId] = useState('')
  const [fromUtc, setFromUtc] = useState('')
  const [toUtc, setToUtc] = useState('')
  const [result, setResult] = useState<ReplayResult | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [ackExport, setAckExport] = useState(false)

  async function runReplay() {
    if (!ackExport) {
      setErr('Confirm that you may export raw protocol bytes (checkbox).')
      return
    }
    setErr(null)
    setLoading(true)
    setResult(null)
    try {
      const body: Record<string, unknown> = { confirmExport: true }
      if (terminalId.trim()) body.terminalId = terminalId.trim()
      if (messageType.trim()) body.messageType = parseInt(messageType, 10)
      if (direction.trim()) body.direction = direction.trim()
      if (processingStatus.trim()) body.processingStatus = parseInt(processingStatus, 10)
      if (sessionCorrelationId.trim()) body.sessionCorrelationId = sessionCorrelationId.trim()
      if (fromUtc.trim()) body.fromUtc = new Date(fromUtc).toISOString()
      if (toUtc.trim()) body.toUtc = new Date(toUtc).toISOString()

      const r = await fetch('/api/dlog/replay?confirm=true', { method: 'POST', headers, body: JSON.stringify(body) })
      if (!r.ok) throw new Error(`Replay ${r.status}`)
      setResult((await r.json()) as ReplayResult)
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'error')
    } finally {
      setLoading(false)
    }
  }

  return (
    <div>
      <p>
        <Link to="/dlog">← DLOG list</Link>
      </p>
      <h1>DLOG replay (debug)</h1>
      <p style={{ color: '#555', maxWidth: 720 }}>
        Returns matching transactions in time order and concatenates raw payloads for lab playback. On-wire modem replay is{' '}
        <strong>HARDWARE_VALIDATION_REQUIRED</strong>.
      </p>

      <fieldset style={{ border: '1px solid #ddd', padding: 12, maxWidth: 720 }}>
        <legend>Filter (all optional)</legend>
        <div style={{ display: 'grid', gap: 8 }}>
          <label>
            Terminal ID
            <input value={terminalId} onChange={(e) => setTerminalId(e.target.value)} style={{ width: '100%' }} />
          </label>
          <label>
            Message type
            <input value={messageType} onChange={(e) => setMessageType(e.target.value)} style={{ width: '100%' }} />
          </label>
          <label>
            Direction (TerminalToHost / HostToTerminal / Unknown)
            <input value={direction} onChange={(e) => setDirection(e.target.value)} style={{ width: '100%' }} />
          </label>
          <label>
            Processing status
            <input
              value={processingStatus}
              onChange={(e) => setProcessingStatus(e.target.value)}
              style={{ width: '100%' }}
            />
          </label>
          <label>
            Session correlation
            <input
              value={sessionCorrelationId}
              onChange={(e) => setSessionCorrelationId(e.target.value)}
              style={{ width: '100%' }}
            />
          </label>
          <label>
            From
            <input type="datetime-local" value={fromUtc} onChange={(e) => setFromUtc(e.target.value)} />
          </label>
          <label>
            To
            <input type="datetime-local" value={toUtc} onChange={(e) => setToUtc(e.target.value)} />
          </label>
        </div>
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, marginTop: 8 }}>
          <input type="checkbox" checked={ackExport} onChange={(e) => setAckExport(e.target.checked)} />
          I acknowledge this exports sensitive raw protocol bytes (lab / authorized use only).
        </label>
        <button type="button" onClick={() => void runReplay()} disabled={loading} style={{ marginTop: 8 }}>
          {loading ? 'Running…' : 'Run replay'}
        </button>
      </fieldset>

      {err && <p style={{ color: 'crimson' }}>{err}</p>}

      {!loading && !err && result && result.transactions.length === 0 && (
        <p style={{ color: '#666', marginTop: 16 }}>No transactions matched the filters.</p>
      )}

      {result && result.transactions.length > 0 && (
        <div style={{ marginTop: 16 }}>
          <p>
            <strong>{result.transactions.length}</strong> record(s), <strong>{result.totalByteLength}</strong> bytes
            concatenated.
          </p>
          <h2>Concatenated hex</h2>
          <pre
            style={{
              fontFamily: 'ui-monospace, monospace',
              fontSize: 12,
              background: '#f6f8fa',
              padding: 12,
              overflow: 'auto',
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-all',
            }}
          >
            {result.concatenatedPayloadHex}
          </pre>
          <h2>Records</h2>
          <ul>
            {result.transactions.map((t) => (
              <li key={t.id}>
                MT {t.messageType} @ {new Date(t.capturedAtUtc).toISOString()} —{' '}
                <Link to={`/dlog/${t.id}`}>{t.id}</Link>
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  )
}
