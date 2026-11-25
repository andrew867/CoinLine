import { useCallback, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiFetch, apiPostBareOk } from '../api/client'
import { carrierReportLabel } from './collectionPolicy'
import './UploadDetail.css'


type RecordRow = {
  id: string
  rawPayloadHex: string
  decodedMetadataJson: string
  createdAtUtc: string
  terminalCall: TerminalCall | null
  terminalStatistics: TerminalStatistics | null
}

type TerminalCall = {
  messageType: number
  sequence: number
  dialedDigits: string
  dialedPrefixOnly: boolean
  carrier: number
  callType: string
  payment: string
  terminalStart: string | null
  durationSeconds: number | null
  amountCharged: number | null
  amountCollected: number | null
}

type CounterGroup = { label: string; counters: { label: string; value: number }[] }
type GraphicalScreenUsage = {
  screenNumber: number
  ownerCode: number
  directionKeyPressCount: number
  keys: { keyNumber: number; ownerCode: number; pressCount: number }[]
}
type TerminalStatistics = {
  messageType: number
  name: string
  periodStart: string | null
  periodEnd: string | null
  groups: CounterGroup[]
  carriers: { reference: number; groups: CounterGroup[] }[]
  carrierFormat: number | null
  graphicalScreens?: GraphicalScreenUsage[] | null
}

type Detail = {
  id: string
  terminalId: string | null
  status: number
  idempotencyKey: string
  isTerminalCallDetail?: boolean
  isTerminalStatistics?: boolean
  rawPayloadHex: string
  decodedMetadataJson: string | null
  relatedDlogTransactionId: string | null
  createdAtUtc: string
  updatedAtUtc: string
  records: RecordRow[]
}

const statusLabel = (n: number) =>
  (
    {
      0: 'Received',
      1: 'Processing',
      2: 'Completed',
      3: 'Failed',
      4: 'Quarantined',
    } as Record<number, string>
  )[n] ?? String(n)

export function UploadDetail() {
  const { id } = useParams()
  const [d, setD] = useState<Detail | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [reviewNote, setReviewNote] = useState('')
  const [metaMode, setMetaMode] = useState<'raw' | 'pretty'>('pretty')

  const load = useCallback(() => {
    if (!id) return
    setErr(null)
    apiFetch(`/api/uploads/${id}`)
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<Detail>
      })
      .then(setD)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
  }, [id])

  useEffect(() => {
    load()
  }, [load])

  async function ingest() {
    if (!id) return
    setErr(null)
    try {
      const r = await apiFetch(`/api/uploads/${id}/ingest`, { method: 'POST' })
      if (!r.ok) throw new Error(`ingest ${r.status}`)
      load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function reprocess() {
    if (!id) return
    if (!window.confirm('Replace derived records by re-running ingestion?')) return
    setErr(null)
    try {
      await apiPostBareOk(`/api/uploads/${id}/reprocess`, { confirm: true })
      load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function markReviewed(e: React.FormEvent) {
    e.preventDefault()
    if (!id) return
    setErr(null)
    try {
      const r = await apiFetch(`/api/uploads/${id}/operator-review`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ note: reviewNote.trim() || null }),
      })
      if (!r.ok) throw new Error(`review ${r.status}`)
      setReviewNote('')
      load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  if (!id) return <p>Missing id.</p>
  if (!d) return err ? <p style={{ color: 'crimson' }}>{err}</p> : <p>Loading…</p>

  const isTerminalUpload = d.isTerminalCallDetail || d.isTerminalStatistics
  const title = d.isTerminalCallDetail ? 'Terminal call detail' : d.isTerminalStatistics ? 'Terminal statistics' : 'Upload batch'
  let metadataBlock = d.decodedMetadataJson ?? '{}'
  if (metaMode === 'pretty') {
    try {
      metadataBlock = JSON.stringify(JSON.parse(metadataBlock), null, 2)
    } catch {
      /* keep raw */
    }
  }

  return (
    <div>
      <p>
        <Link to="/uploads">← Uploads</Link>
      </p>
      <h1>{title}</h1>
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      <p>
        <strong>{statusLabel(d.status)}</strong>
        {d.terminalId && (
          <>
            {' '}
            · <Link to={`/terminals/${d.terminalId}`}>View phone</Link>
          </>
        )}
      </p>
      {!isTerminalUpload && <p style={{ fontSize: 13, color: '#444' }}>
        Idempotency <code>{d.idempotencyKey}</code>
      </p>}

      {!isTerminalUpload && <div style={{ display: 'flex', gap: 8, marginBottom: 16, flexWrap: 'wrap' }}>
        <button type="button" onClick={() => void ingest()}>
          Run ingest
        </button>
        <button type="button" onClick={() => void reprocess()}>
          Reprocess (confirm)
        </button>
      </div>}

      <form onSubmit={markReviewed} style={{ marginBottom: 24 }}>
        <label>
          Operator review note{' '}
          <input value={reviewNote} onChange={(e) => setReviewNote(e.target.value)} style={{ minWidth: 280 }} />
        </label>{' '}
        <button type="submit">Save review</button>
      </form>

      {d.isTerminalStatistics ? (
        <section aria-label="Received statistics" data-testid="upload-records">
          {d.records.map(({ id: recordId, terminalStatistics }) => terminalStatistics &&
            <StatisticsReport key={recordId} report={terminalStatistics} />)}
          <p>Received <time dateTime={d.createdAtUtc}>{new Date(d.createdAtUtc).toLocaleString()}</time></p>
        </section>
      ) : d.isTerminalCallDetail ? (
        <section aria-labelledby="terminal-call-details-heading">
          <h2 id="terminal-call-details-heading" data-testid="upload-records">Received call record</h2>
          {d.records.map(({ id: recordId, terminalCall }) => terminalCall && <TerminalCallDetails key={recordId} call={terminalCall} />)}
          <p>Received <time dateTime={d.createdAtUtc}>{new Date(d.createdAtUtc).toLocaleString()}</time></p>
        </section>
      ) : (
        <>
          <h2>Decoded metadata</h2>
          <p>
            <button type="button" onClick={() => setMetaMode('raw')}>Raw</button>{' '}
            <button type="button" onClick={() => setMetaMode('pretty')}>Pretty</button>
          </p>
          <pre style={{ background: '#f6f8fa', padding: 12, fontSize: 12, maxHeight: 320, overflow: 'auto' }}>
            {metadataBlock}
          </pre>

          <h2>Raw payload (hex)</h2>
          <pre style={{ background: '#f6f8fa', padding: 12, fontSize: 11, wordBreak: 'break-all' }}>{d.rawPayloadHex}</pre>

          <h2 data-testid="upload-records">Derived records ({d.records.length})</h2>
          {d.records.length === 0 && <p style={{ color: '#666' }}>No records yet. Run ingest to process this batch.</p>}
          {d.records.length > 0 && (
            <ol style={{ paddingLeft: 18 }}>
              {d.records.map((r) => (
                <li key={r.id} style={{ marginBottom: 16 }}>
                  <div style={{ fontSize: 12 }}><code>{r.id}</code></div>
                  <pre style={{ fontSize: 11, background: '#fafafa', padding: 8, wordBreak: 'break-all' }}>
                    {r.rawPayloadHex}
                  </pre>
                  <pre style={{ fontSize: 11, background: '#fff', padding: 8 }}>{r.decodedMetadataJson}</pre>
                </li>
              ))}
            </ol>
          )}
        </>
      )}
    </div>
  )
}

function StatisticsReport({ report }: { report: TerminalStatistics }) {
  const period = (value: string | null) => value?.replace('T', ' ') ?? 'Unavailable'

  return <article className="upload-statistics">
    <header className="upload-statistics-header">
      <h2>{report.name}</h2>
      <dl>
        <div><dt>Period starts (phone clock)</dt><dd>{period(report.periodStart)}</dd></div>
        <div><dt>Period ends (phone clock)</dt><dd>{period(report.periodEnd)}</dd></div>
        <div><dt>Record type</dt><dd>MT{report.messageType}</dd></div>
        {report.carrierFormat != null && <div><dt>Carrier record version</dt><dd>{report.carrierFormat}</dd></div>}
      </dl>
    </header>
    {report.groups.length > 0 && <CounterGroups groups={report.groups} />}
    {report.carriers.map(({ reference, groups }, index) => <details key={`${reference}-${index}`} open={index === 0}>
      <summary>{carrierReportLabel(report.messageType, reference)}</summary>
      <CounterGroups groups={groups} />
    </details>)}
    {report.messageType === 153 ? <GraphicalUsageReport screens={report.graphicalScreens ?? []} />
      : report.groups.length === 0 && report.carriers.length === 0 && <p>No carrier entries in this report.</p>}
  </article>
}

function GraphicalUsageReport({ screens }: { screens: GraphicalScreenUsage[] }) {
  return <section aria-label="Graphical screen usage">
    <p>Each screen's reported owner and key counts are captured for this reporting period.
      A phone can upload more than one record for the same period. Counts are key presses, not advertisement impressions.</p>
    {screens.length === 0 ? <p>No screen entries in this report.</p> :
      <div className="graphical-usage-scroll">
        <table className="graphical-usage-table">
          <caption>Graphical usage by screen</caption>
          <thead><tr>
            <th scope="col">Screen</th><th scope="col">Screen owner code</th>
            {Array.from({ length: 6 }, (_, index) => <th key={index} scope="col">Softkey {index + 1}</th>)}
            <th scope="col">Direction keys</th>
          </tr></thead>
          <tbody>{screens.map((screen, index) => <tr key={`${screen.screenNumber}-${index}`}>
            <th scope="row">Screen {screen.screenNumber}</th><td>{screen.ownerCode}</td>
            {Array.from({ length: 6 }, (_, index) => {
              const key = screen.keys.find(item => item.keyNumber === index + 1)
              return <td key={index}>{key ? <>
                <strong>{key.pressCount.toLocaleString()}</strong>
                <span>Owner {key.ownerCode}</span>
              </> : 'Unavailable'}</td>
            })}
            <td>{screen.directionKeyPressCount.toLocaleString()}</td>
          </tr>)}</tbody>
        </table>
      </div>}
  </section>
}

function CounterGroups({ groups }: { groups: CounterGroup[] }) {
  return <div className="upload-statistics-groups">
    {groups.map(({ label, counters }) => <section key={label}>
      <h3>{label}</h3>
      <dl>{counters.map(({ label: counter, value }) => <div key={counter}>
        <dt>{counter}</dt><dd>{value.toLocaleString()}</dd>
      </div>)}</dl>
    </section>)}
  </div>
}

function TerminalCallDetails({ call }: { call: TerminalCall }) {
  const rows = [
    ['Dialed number', call.dialedDigits ? call.dialedDigits + (call.dialedPrefixOnly ? ' (prefix)' : '') : 'Unavailable'],
    ['Started (phone clock)', call.terminalStart?.replace('T', ' ') ?? 'Unavailable'],
    ['Duration', call.durationSeconds == null ? 'Unavailable' : `${call.durationSeconds} s`],
    ['Call type', call.callType],
    ['Payment', call.payment],
    ['Carrier code', call.carrier],
    ['Charged (terminal units)', call.amountCharged ?? 'Unavailable'],
    ['Collected (terminal units)', call.amountCollected ?? 'Unavailable'],
    ['Record sequence', call.sequence],
    ['Record type', `MT${call.messageType}`],
  ]
  return <dl style={{ display: 'grid', gridTemplateColumns: 'minmax(10rem, max-content) minmax(0, 1fr)', gap: '0.5rem 1rem' }}>
    {rows.map(([term, value]) => <div key={term} style={{ display: 'contents' }}>
      <dt>{term}</dt><dd style={{ margin: 0 }}>{value}</dd>
    </div>)}
  </dl>
}
