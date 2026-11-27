import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { HexViewer } from '../components/HexViewer'
import { JsonPanel } from '../components/JsonPanel'
import { apiDownload, apiFetch } from '../api/client'
import { alarmSeverityLabel } from './fieldEventLabels'
import { ReportedStatusGroups, type StatusGroup } from './ReportedStatusGroups'


type Detail = {
  id: string
  terminalId: string | null
  terminalName: string | null
  nccSessionId: string | null
  liveVersionObservation?: boolean
  direction: number
  messageType: number
  messageTypeName: string
  isUnknownMessageType: boolean
  processingStatus: number
  immediateClear: boolean
  rawPayloadHex: string
  decodedJson: string
  terminalStatus?: { groups: StatusGroup[]; unmappedSetBitCount: number; profile: string; rawStatusHex: string; serialNumber: string | null } | null
  fieldEvent: { title: string; category: string; summary: string | null; phoneTime: string | null; operatorDescription?: string | null; severity?: number | null } | null
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
  const [downloadError, setDownloadError] = useState('')

  useEffect(() => {
    if (!id) return
    let cancelled = false
    ;(async () => {
      setErr(null)
      setDetail(null)
      setLoading(true)
      setDownloadError('')
      try {
        const r = await apiFetch(`/api/dlog/transactions/${id}`)
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

  const fieldEvent = detail.fieldEvent
  const isAlarm = fieldEvent?.category === 'alarm'
  const reportedStatus = detail.terminalStatus
  const fields = decodedFields(detail.decodedJson)

  async function download(messageId: string) {
    setDownloadError('')
    try {
      await apiDownload(`/api/dlog/transactions/${messageId}/payload`, `message-${messageId}.bin`)
    } catch (error) {
      setDownloadError(error instanceof Error ? error.message : 'Could not download the raw bytes.')
    }
  }

  const rawBytes = (
    <>
      <h2>Raw bytes</h2>
      <HexViewer hex={detail.rawPayloadHex} />
      <p>
        <button type="button" onClick={() => void download(detail.id)}>
          Download raw bytes
        </button>
      </p>
      {downloadError && <p role="alert">{downloadError}</p>}
    </>
  )
  const jsonFields = fields != null && (
    <pre style={{ background: '#f6f8fa', padding: '0.75rem', overflow: 'auto', maxWidth: 720 }}>
      {JSON.stringify(fields, null, 2)}
    </pre>
  )

  return (
    <div>
      <p>
        <Link to={fieldEvent ? '/field-events' : '/dlog'}>{fieldEvent ? '← Terminal status' : '← Message log'}</Link>
      </p>
      <h1>{fieldEvent?.title ?? 'Stored message'}</h1>
      {isAlarm && <h2>Decoded alarm</h2>}
      {!isAlarm && fieldEvent?.summary && <p className="page-intro">{fieldEvent.summary}</p>}
      {reportedStatus && <section className="current-phone-status" aria-label="Decoded terminal status">
        <h2>Decoded terminal status</h2>
        <p>Reported serial number: {reportedStatus.serialNumber ?? 'Not available'}. Unresolved asserted bits: {reportedStatus.unmappedSetBitCount}.</p>
        <ReportedStatusGroups groups={reportedStatus.groups} profile={reportedStatus.profile} rawStatusHex={reportedStatus.rawStatusHex} />
      </section>}
      {!isAlarm && !reportedStatus && jsonFields && <><h2>Decoded fields</h2>{jsonFields}</>}
      {detail.isUnknownMessageType && (
        <p role="status">This message type is not in the catalog. The raw bytes are still stored.</p>
      )}
      <dl style={{ display: 'grid', gridTemplateColumns: '160px 1fr', gap: 8, maxWidth: 720 }}>
        {isAlarm && (
          <>
            <dt>Alarm</dt>
            <dd><strong>{fieldEvent?.summary ?? 'Alarm details unavailable'}</strong></dd>
            {detail.messageType === 7 && (
              <>
                <dt>Phone-reported time</dt>
                <dd>{fieldEvent?.phoneTime
                  ? <time dateTime={fieldEvent.phoneTime}>{fieldEvent.phoneTime.replace('T', ' ')}</time>
                  : 'Unavailable'}</dd>
              </>
            )}
          </>
        )}
        {fieldEvent?.operatorDescription && <><dt>Operator description</dt><dd>{fieldEvent.operatorDescription}</dd></>}
        {fieldEvent?.severity != null && <><dt>Current severity</dt><dd>{alarmSeverityLabel(fieldEvent.severity)}</dd></>}
        <dt>Recorded by CoinLine</dt>
        <dd><time dateTime={detail.capturedAtUtc}>{new Date(detail.capturedAtUtc).toLocaleString()}</time></dd>
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
        {detail.messageType === 60 && <>
          <dt>Version evidence</dt>
          <dd>{detail.liveVersionObservation ? 'Observed on a matched NCC connection' : 'Imported or historical report; not eligible for version-based scheduling'}</dd>
        </>}
        <dt>Status</dt>
        <dd>{statusWord(detail.processingStatus)}</dd>
        <dt>Session</dt>
        <dd>{detail.sessionCorrelationId ?? '—'}</dd>
      </dl>

      {!isAlarm && !reportedStatus && rawBytes}

      <details>
        <summary>Technical record</summary>
        {(isAlarm || reportedStatus) && rawBytes}
        {(isAlarm || reportedStatus) && jsonFields && <><h2>{reportedStatus ? 'Original decoded JSON' : 'Decoded JSON'}</h2>{jsonFields}</>}
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

function statusWord(status: number): string {
  if (status === 0) return 'Stored'
  if (status === 1) return 'Decoded'
  if (status === 2) return 'Linked'
  if (status === 3) return 'Exported'
  return 'Stored'
}
