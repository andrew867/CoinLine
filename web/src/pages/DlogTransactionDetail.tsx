import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { HexViewer } from '../components/HexViewer'
import { JsonPanel } from '../components/JsonPanel'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Detail = {
  id: string
  terminalId: string | null
  terminalName: string | null
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

  const title = fieldTitle(detail.messageType) ?? 'Stored message'
  const fromPhone = fieldTitle(detail.messageType) != null

  return (
    <div>
      <p>
        <Link to={fromPhone ? '/field-events' : '/dlog'}>{fromPhone ? '← Alarms' : '← Message log'}</Link>
      </p>
      <h1>{title}</h1>
      <p className="page-intro">
        {fromPhone
          ? 'This is the message the phone sent. Documented alarm and cash fields are shown when the body is long enough; raw bytes stay authoritative.'
          : 'This is a message stored from a phone session.'}
      </p>
      {(() => {
        const fields = decodedFields(detail.decodedJson)
        if (fields == null) return null
        return (
          <>
            <h2>Decoded fields</h2>
            <pre style={{ background: '#f6f8fa', padding: '0.75rem', overflow: 'auto', maxWidth: 720 }}>
              {JSON.stringify(fields, null, 2)}
            </pre>
          </>
        )
      })()}
      {detail.isUnknownMessageType && (
        <p role="status">This message type is not in the catalog. The raw bytes are still stored.</p>
      )}
      <dl style={{ display: 'grid', gridTemplateColumns: '160px 1fr', gap: 8, maxWidth: 720 }}>
        <dt>When</dt>
        <dd>{new Date(detail.capturedAtUtc).toLocaleString()}</dd>
        <dt>Phone</dt>
        <dd>
          {detail.terminalId ? (
            <Link to={`/terminals/${detail.terminalId}`}>{detail.terminalName || detail.terminalId}</Link>
          ) : (
            '—'
          )}
        </dd>
        <dt>Direction</dt>
        <dd>{detail.direction === 1 ? 'From the phone' : detail.direction === 2 ? 'To the phone' : 'Unknown'}</dd>
        <dt>Status</dt>
        <dd>{statusWord(detail.processingStatus)}</dd>
        <dt>Session</dt>
        <dd>{detail.sessionCorrelationId ?? '—'}</dd>
      </dl>

      <h2>Raw bytes</h2>
      <HexViewer hex={detail.rawPayloadHex} />
      <p>
        <a href={`/api/dlog/transactions/${detail.id}/payload`} download={`message-${detail.id}.bin`}>
          Download raw bytes
        </a>
      </p>

      <details>
        <summary>Technical record</summary>
        <dl style={{ display: 'grid', gridTemplateColumns: '160px 1fr', gap: 8, maxWidth: 720 }}>
          <dt>Record id</dt>
          <dd>{detail.id}</dd>
          <dt>Message type</dt>
          <dd>
            {detail.messageType} — {detail.messageTypeName}
          </dd>
          <dt>Session id</dt>
          <dd>{detail.nccSessionId ?? '—'}</dd>
          <dt>Idempotency key</dt>
          <dd style={{ wordBreak: 'break-all' }}>{detail.idempotencyKey}</dd>
        </dl>
        <h2>Notes</h2>
          {detail.parseDiagnostics?.length ? (
          <ul>
            {detail.parseDiagnostics.map((d, i) => (
              <li key={i} style={{ marginBottom: 8 }}>
                {d.message}
              </li>
            ))}
          </ul>
        ) : (
          <p>None stored.</p>
        )}
        <JsonPanel title="Message catalog" url="/api/dlog/message-types" />
      </details>
    </div>
  )
}

function decodedFields(decodedJson: string): unknown | null {
  try {
    const parsed = JSON.parse(decodedJson) as { fields?: unknown; Fields?: unknown }
    return parsed.fields ?? parsed.Fields ?? null
  } catch {
    return null
  }
}

function fieldTitle(messageType: number): string | null {
  if (messageType === 7) return 'Alarm'
  if (messageType === 10) return 'Phone status'
  if (messageType === 16) return 'Alarm acknowledgement'
  if (messageType === 38) return 'Cash box status'
  if (messageType === 39) return 'Attention callback'
  if (messageType >= 40 && messageType <= 47) {
    if (messageType === 42) return 'Attention required (CDR upload)'
    if (messageType === 44) return 'Attention required (tables)'
    return 'Attention required'
  }
  if (messageType === 51) return 'Cash box collection'
  if (messageType === 83) return 'Phone error'
  return null
}

function statusWord(status: number): string {
  if (status === 0) return 'Stored'
  if (status === 1) return 'Decoded'
  if (status === 2) return 'Linked'
  if (status === 3) return 'Exported'
  return 'Stored'
}
