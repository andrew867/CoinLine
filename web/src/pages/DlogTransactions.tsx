import type { CSSProperties } from 'react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Row = {
  id: string
  terminalId: string | null
  nccSessionId: string | null
  direction: number
  messageType: number
  messageTypeName: string
  isUnknownMessageType: boolean
  processingStatus: number
  immediateClear: boolean
  capturedAtUtc: string
  sessionCorrelationId: string | null
  rawPayloadHex: string
}

export function DlogTransactions() {
  const [searchParams] = useSearchParams()
  const [rows, setRows] = useState<Row[] | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [terminalId, setTerminalId] = useState('')
  const [messageType, setMessageType] = useState('')
  const [direction, setDirection] = useState('')
  const [processingStatus, setProcessingStatus] = useState('')
  const [sessionCorrelationId, setSessionCorrelationId] = useState('')
  const [fromUtc, setFromUtc] = useState('')
  const [toUtc, setToUtc] = useState('')

  useEffect(() => {
    const t = searchParams.get('terminal')
    if (t) setTerminalId(t)
  }, [searchParams])

  const query = useMemo(() => {
    const p = new URLSearchParams()
    if (terminalId.trim()) p.set('terminalId', terminalId.trim())
    if (messageType.trim()) p.set('messageType', messageType.trim())
    if (direction.trim()) p.set('direction', direction.trim())
    if (processingStatus.trim()) p.set('processingStatus', processingStatus.trim())
    if (sessionCorrelationId.trim()) p.set('sessionCorrelationId', sessionCorrelationId.trim())
    if (fromUtc.trim()) p.set('fromUtc', new Date(fromUtc).toISOString())
    if (toUtc.trim()) p.set('toUtc', new Date(toUtc).toISOString())
    return p.toString()
  }, [terminalId, messageType, direction, processingStatus, sessionCorrelationId, fromUtc, toUtc])

  const refresh = useCallback(async () => {
    setErr(null)
    setLoading(true)
    try {
      const url = query ? `/api/dlog/transactions?${query}` : '/api/dlog/transactions'
      const r = await fetch(url, { headers })
      if (!r.ok) throw new Error(`List ${r.status}`)
      setRows((await r.json()) as Row[])
    } catch (e) {
      setErr(e instanceof Error ? e.message : 'error')
      setRows(null)
    } finally {
      setLoading(false)
    }
  }, [query])

  useEffect(() => {
    void refresh()
  }, [refresh])

  return (
    <div>
      <h1>DLOG transactions</h1>
      <p style={{ color: '#555', maxWidth: 720 }}>
        Raw bytes are preserved exactly as ingested. Unknown message types are retained and flagged — decoding does not drop
        octets.
      </p>
      <p>
        <Link to="/dlog/replay-debug">Replay / debug</Link>
      </p>

      <fieldset style={{ border: '1px solid #ddd', padding: 12, marginBottom: 16, maxWidth: 900 }}>
        <legend>Filters</legend>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(200px, 1fr))', gap: 8 }}>
          <label>
            Terminal ID
            <input value={terminalId} onChange={(e) => setTerminalId(e.target.value)} style={{ width: '100%' }} />
          </label>
          <label>
            Message type (int)
            <input value={messageType} onChange={(e) => setMessageType(e.target.value)} style={{ width: '100%' }} />
          </label>
          <label>
            Direction (0–2)
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
            From (local)
            <input type="datetime-local" value={fromUtc} onChange={(e) => setFromUtc(e.target.value)} />
          </label>
          <label>
            To (local)
            <input type="datetime-local" value={toUtc} onChange={(e) => setToUtc(e.target.value)} />
          </label>
        </div>
        <button type="button" onClick={() => void refresh()} style={{ marginTop: 8 }}>
          Apply
        </button>
      </fieldset>

      {loading && <p>Loading…</p>}
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!loading && !err && rows && rows.length === 0 && (
        <p style={{ color: '#666' }}>No DLOG transactions match the current filters.</p>
      )}
      {!loading && !err && rows && rows.length > 0 && (
        <table style={{ borderCollapse: 'collapse', width: '100%', fontSize: 14 }}>
          <thead>
            <tr>
              <th style={th}>Captured</th>
              <th style={th}>MT</th>
              <th style={th}>Name</th>
              <th style={th}>Dir</th>
              <th style={th}>Status</th>
              <th style={th}>Unknown</th>
              <th style={th}>Session</th>
              <th style={th} />
            </tr>
          </thead>
          <tbody>
            {rows.map((x) => (
              <tr key={x.id}>
                <td style={td}>{new Date(x.capturedAtUtc).toISOString()}</td>
                <td style={td}>{x.messageType}</td>
                <td style={td}>{x.messageTypeName}</td>
                <td style={td}>{x.direction}</td>
                <td style={td}>{x.processingStatus}</td>
                <td style={td}>{x.isUnknownMessageType ? 'yes' : ''}</td>
                <td style={td}>{x.sessionCorrelationId ?? ''}</td>
                <td style={td}>
                  <Link to={`/dlog/${x.id}`}>Detail</Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}

const th: CSSProperties = { textAlign: 'left', borderBottom: '1px solid #ccc', padding: 6 }
const td: CSSProperties = { borderBottom: '1px solid #eee', padding: 6, verticalAlign: 'top' }
