import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { HexViewer } from '../components/HexViewer'
import { JsonPanel } from '../components/JsonPanel'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Detail = {
  id: string
  terminalId: string | null
  nccSessionId: string | null
  direction: number
  messageType: number
  messageTypeName: string
  isUnknownMessageType: boolean
  processingStatus: number
  immediateClear: boolean
  rawPayloadHex: string
  decodedJson: string
  idempotencyKey: string
  capturedAtUtc: string
  sessionCorrelationId: string | null
  parseDiagnostics: { severity: string; code: string; message: string; detail: string }[]
  correlationLinks: { id: string; requestTransactionId: string; responseTransactionId: string; linkRule: string }[]
}

export function DlogTransactionDetail() {
  const { id } = useParams()
  const [detail, setDetail] = useState<Detail | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    if (!id) return
    let cancelled = false
    ;(async () => {
      setErr(null)
      setDetail(null)
      setLoading(true)
      try {
        const r = await fetch(`/api/dlog/transactions/${id}`, { headers })
        if (!r.ok) throw new Error(`Detail ${r.status}`)
        const j = (await r.json()) as Detail
        if (!cancelled) setDetail(j)
      } catch (e) {
        if (!cancelled) setErr(e instanceof Error ? e.message : 'error')
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()
    return () => {
      cancelled = true
    }
  }, [id])

  if (!id) return <p>Missing id</p>
  if (loading) return <p>Loading…</p>
  if (err) return <p style={{ color: 'crimson' }}>{err}</p>
  if (!detail) return <p>No data.</p>

  return (
    <div>
      <p>
        <Link to="/dlog">← DLOG list</Link>
      </p>
      <h1>DLOG transaction</h1>
      {detail.isUnknownMessageType && (
        <div
          style={{
            background: '#fff3cd',
            border: '1px solid #ffc107',
            padding: 12,
            marginBottom: 12,
            maxWidth: 720,
          }}
        >
          <strong>Unknown message type</strong> — raw payload is still shown below. Extend the registry when the firmware
          variant is confirmed (see COMPRESS_LCD / NPA table aliasing in docs).
        </div>
      )}
      <dl style={{ display: 'grid', gridTemplateColumns: '160px 1fr', gap: 8, maxWidth: 720 }}>
        <dt>ID</dt>
        <dd>{detail.id}</dd>
        <dt>Message type</dt>
        <dd>
          {detail.messageType} — {detail.messageTypeName}
        </dd>
        <dt>Direction</dt>
        <dd>{detail.direction}</dd>
        <dt>Processing status</dt>
        <dd>{detail.processingStatus}</dd>
        <dt>Immediate clear</dt>
        <dd>{detail.immediateClear ? 'yes' : 'no'}</dd>
        <dt>Terminal</dt>
        <dd>{detail.terminalId ?? '—'}</dd>
        <dt>NCC session</dt>
        <dd>{detail.nccSessionId ?? '—'}</dd>
        <dt>Session correlation</dt>
        <dd>{detail.sessionCorrelationId ?? '—'}</dd>
        <dt>Idempotency key</dt>
        <dd style={{ wordBreak: 'break-all' }}>{detail.idempotencyKey}</dd>
        <dt>Captured</dt>
        <dd>{new Date(detail.capturedAtUtc).toISOString()}</dd>
      </dl>

      <h2>Raw payload (hex)</h2>
      <HexViewer hex={detail.rawPayloadHex} />
      <p>
        <a href={`/api/dlog/transactions/${detail.id}/payload`} download={`dlog-${detail.id}.bin`}>
          Download raw bytes
        </a>
      </p>

      <h2>Decoded metadata (non-authoritative)</h2>
      <pre style={{ fontFamily: 'ui-monospace, monospace', fontSize: 12, background: '#f6f8fa', padding: 12 }}>
        {detail.decodedJson}
      </pre>

      <h2>Parse diagnostics</h2>
      {detail.parseDiagnostics?.length ? (
        <ul>
          {detail.parseDiagnostics.map((d, i) => (
            <li key={i} style={{ marginBottom: 8 }}>
              <strong>{d.severity}</strong> {d.code}: {d.message}
              {d.detail ? <div style={{ color: '#555', fontSize: 13 }}>{d.detail}</div> : null}
            </li>
          ))}
        </ul>
      ) : (
        <p>None stored.</p>
      )}

      <h2>Correlation links</h2>
      {detail.correlationLinks?.length ? (
        <ul>
          {detail.correlationLinks.map((l) => (
            <li key={l.id}>
              {l.linkRule} — request {l.requestTransactionId} ↔ response {l.responseTransactionId}
            </li>
          ))}
        </ul>
      ) : (
        <p>None.</p>
      )}

      <h2>Registry (reference)</h2>
      <JsonPanel title="Message types" url="/api/dlog/message-types" />
    </div>
  )
}
