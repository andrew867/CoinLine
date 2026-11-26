import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiFetch, apiPostBare, apiPostBareOk } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import { StatusBadge } from '../components/StatusBadge'
import './TableSetConfiguration.css'


type Step = {
  id: string
  stepIndex: number
  itemStatus: number
  hostDownloadPhase: number
  hostDownloadPhaseName: string
  lastAckStatus: string
  succeeded: boolean
  errorDetail: string | null
  tableVersionId: string
  tableNumber?: number
  tableDefinitionName: string
  payloadSha256Hex: string
}

type Detail = {
  id: string
  terminalId: string
  nccSessionId: string | null
  scheduleId: string | null
  tableSetId: string
  tableSetName: string | null
  status: number
  scope: number
  partialDefinitionIdsJson: string | null
  retryCount: number
  lastError: string | null
  diagnosticsJson: string | null
  completedAtUtc: string | null
  createdAtUtc: string
  sessionEvidence?: {
    status: number; startedAtUtc: string; endedAtUtc: string | null; terminalClearObservedAtUtc: string | null
    terminalClearSequence: number | null; releaseOutcome: string | null; releaseElapsedMilliseconds: number | null
  } | null
  timeline: Step[]
}

const acknowledgementLabels: Record<string, string> = {
  ack: 'Table acknowledged',
  pending_terminal_ack: 'Awaiting terminal',
  missing: 'Acknowledgement missing',
  missing_link_ack: 'Link acknowledgement missing',
  missing_table_ack: 'Table acknowledgement missing (MT14)',
  nak: 'Table rejected',
  link_nak: 'Link frame rejected',
  not_sent: 'Not sent',
  interrupted: 'Interrupted',
  cancelled: 'Cancelled',
}

const itemStatusLabels: Record<number, string> = {
  0: 'Pending', 1: 'Queued', 2: 'In progress', 3: 'Succeeded', 4: 'Failed', 5: 'Cancelled', 6: 'Skipped',
}
const releaseLabels: Record<string, string> = {
  peer_eof: 'Peer closed the transport after terminal clear', deadline: 'Host drain deadline reached',
  peer_error: 'Transport error during release', shutdown: 'Listener shut down during release',
}
const sessionLabels: Record<number, string> = { 0: 'Open (stored session)', 1: 'Closed', 2: 'Archived' }
function utcTime(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.valueOf()) ? value : date.toISOString()
}
export function DownloadDetail() {
  const { id } = useParams()
  const [d, setD] = useState<Detail | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(false)
  const [refresh, setRefresh] = useState(0)

  const load = () => setRefresh(count => count + 1)

  useEffect(() => {
    if (!id) return
    const abort = new AbortController()
    setErr(null); setRefreshing(true)
    void apiFetch(`/api/downloads/${id}`, { signal: abort.signal })
      .then((r) => {
        if (!r.ok) throw new Error(`Could not load download (HTTP ${r.status}).`)
        return r.json() as Promise<Detail>
      })
      .then(result => { if (!abort.signal.aborted) setD(result) })
      .catch(error => { if (!abort.signal.aborted) setErr(error instanceof Error ? error.message : 'Could not load download.') })
      .finally(() => { if (!abort.signal.aborted) setRefreshing(false) })
    return () => abort.abort()
  }, [id, refresh])

  async function cancel() {
    if (!id) return
    if (
      !window.confirm(
        'Cancel this host download batch? Records already accepted by the phone will not be erased.'
      )
    )
      return
    setErr(null)
    try {
      await apiPostBareOk(`/api/downloads/${id}/cancel`, { confirm: true })
      load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function retry() {
    if (!id) return
    if (
      !window.confirm(
        'Prepare a replacement batch from this failed or cancelled run? The phone must pick up the new batch during an eligible call.'
      )
    )
      return
    setErr(null)
    try {
      const response = await apiPostBare(`/api/downloads/${id}/retry`, { confirm: true })
      const j = await response.json().catch(() => null)
      if (!response.ok) throw new Error(j?.error ?? `Could not retry the download (HTTP ${response.status}).`)
      window.location.href = `/downloads/${j.id}`
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  if (!id) return <p>Missing id.</p>
  if (!d || d.id !== id) return err ? <p role="alert" style={{ color: 'crimson' }}>{err}</p> : <p>Loading…</p>

  const statusLabel = (n: number) =>
    (
      {
        0: 'Pending',
        1: 'Running',
        2: 'Completed',
        3: 'Failed',
        4: 'Cancelled',
        5: 'Preparing',
      } as Record<number, string>
    )[n] ?? String(n)

  const canUpdate = canPerform('downloads', op.update)
  const canCancel = canUpdate && !d.scheduleId && (d.status === 0 || (d.status === 1 && !d.nccSessionId) || d.status === 5)
  const canRetry = canUpdate && !d.scheduleId && (d.status === 3 || d.status === 4)
  const acknowledged = d.timeline.filter(step => step.succeeded && step.lastAckStatus === 'ack').length
  const failed = d.timeline.filter(step => step.itemStatus === 4).length
  const notSent = d.timeline.filter(step => step.lastAckStatus === 'not_sent').length
  const session = d.sessionEvidence

  return (
    <div>
      <p>
        <Link to="/downloads">← Downloads</Link>
      </p>
      <h1>Download batch</h1>
      {err && <p role="alert" style={{ color: 'crimson' }}>{err}</p>}
      <p>
        Host transfer: <StatusBadge label={statusLabel(d.status)} variant={d.status === 2 ? 'ok' : d.status === 3 ? 'bad' : 'neutral'} /> · Terminal <code>{d.terminalId}</code>
      </p>
      {d.nccSessionId && <p>NCC session {canPerform('ncc-sessions', op.read)
        ? <Link to={`/ncc-sessions?sessionId=${encodeURIComponent(d.nccSessionId)}`}><code>{d.nccSessionId}</code></Link> : <code>{d.nccSessionId}</code>}</p>}
      {d.scheduleId && <p><Link to={`/table-sets/${d.tableSetId}`}>Manage scheduled download</Link></p>}
      <p>
        Set: {d.tableSetName ?? d.tableSetId} · Scope: {d.scope === 1 ? 'Partial' : 'Full'} · Retries: {d.retryCount}
      </p>
      {d.lastError && <p role="alert">{d.lastError}</p>}
      <section className="table-config-panel" aria-label="Host transfer evidence">
        <h2>Host transfer evidence</h2>
        {d.timeline.length > 0 ? <>
          <p><strong>{acknowledged} of {d.timeline.length} records acknowledged</strong></p>
          <progress aria-label="Record acknowledgements" max={d.timeline.length} value={acknowledged} style={{ width: 'min(100%, 32rem)' }} />
          <p>{failed} failed · {notSent} explicitly not sent</p>
        </> : <p>No prepared record steps retained.</p>}
        {d.status === 2 && (d.timeline.length === 0 || acknowledged !== d.timeline.length) &&
          <p role="status">The host marked this batch completed, but the retained timeline does not establish acknowledgement of every record.</p>}
        {d.completedAtUtc && <p>Host batch finished: <time dateTime={d.completedAtUtc}>{utcTime(d.completedAtUtc)}</time></p>}
        {session ? <dl>
          <dt>Linked NCC session</dt><dd>{sessionLabels[session.status] ?? 'Unknown stored state'}</dd>
          <dt>Session start</dt><dd><time dateTime={session.startedAtUtc}>{utcTime(session.startedAtUtc)}</time></dd>
          {session.endedAtUtc && <><dt>Recorded session end</dt><dd><time dateTime={session.endedAtUtc}>{utcTime(session.endedAtUtc)}</time></dd></>}
          <dt>Terminal-initiated clear</dt><dd>{session.terminalClearObservedAtUtc
            ? <>Observed{session.terminalClearSequence != null && `, frame ${session.terminalClearSequence}`} at <time dateTime={session.terminalClearObservedAtUtc}>{utcTime(session.terminalClearObservedAtUtc)}</time></>
            : 'Not recorded in the retained session'}</dd>
          {session.terminalClearObservedAtUtc && <><dt>Transport release</dt><dd>{releaseLabels[session.releaseOutcome ?? ''] ?? 'No retained release observation'}
            {session.releaseElapsedMilliseconds != null && Number.isFinite(session.releaseElapsedMilliseconds) && ` (${session.releaseElapsedMilliseconds.toFixed(1)} ms host time)`}</dd></>}
        </dl> : <p>{d.nccSessionId ? 'No linked session evidence is available in this response.' : 'No terminal session assigned to this batch yet.'}</p>}
        <p className="page-intro">A recorded clear or transport release is host-side protocol evidence, not proof that the terminal completed INSTALL.</p>
      </section>
      <section className="table-config-panel" aria-label="Terminal service verification">
        <h2>Terminal service</h2>
        <StatusBadge label="Not verified by this download" variant="neutral" />
        <p>Record acknowledgements establish the transfer, not successful installation, warm restart, device checks or actual service. Verify the terminal display and behavior separately.</p>
        {canPerform('phones', op.read) && <Link to={`/terminals/${d.terminalId}`}>View the phone's reported status</Link>}
      </section>
      {d.diagnosticsJson && <details><summary>Host diagnostic metadata</summary>
        <pre style={{ background: '#f6f8fa', padding: 12, fontSize: 12, whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}>{d.diagnosticsJson}</pre>
      </details>}
      <div style={{ display: 'flex', gap: 8, marginBottom: 16 }}>
        <button type="button" disabled={refreshing} onClick={load}>{refreshing ? 'Refreshing…' : 'Refresh status'}</button>
        {canCancel && (
          <button type="button" onClick={() => void cancel()}>
            Cancel batch
          </button>
        )}
        {canRetry && (
          <button type="button" onClick={() => void retry()}>
            Retry (new batch)
          </button>
        )}
      </div>

      <h2 data-testid="download-batch-timeline">Timeline</h2>
      {d.timeline.length === 0 && (
        <p style={{ color: '#666' }}>No ordered steps yet (batch may still be preparing).</p>
      )}
      <ol style={{ paddingLeft: 18 }}>
        {d.timeline.map((s) => (
          <li key={s.id} style={{ marginBottom: 12 }}>
            <div>
              Step {s.stepIndex}: <strong>{s.tableDefinitionName}</strong>{s.tableNumber != null && <> · <code>MT{s.tableNumber}</code></>}
            </div>
            <div style={{ fontSize: 13, color: '#444' }}>
              Host phase: {s.hostDownloadPhaseName} · Acknowledgement: {acknowledgementLabels[s.lastAckStatus] ?? s.lastAckStatus} · Item status:{' '}
              {itemStatusLabels[s.itemStatus] ?? s.itemStatus}
            </div>
            {s.errorDetail && <div>{s.errorDetail}</div>}
            <div style={{ fontSize: 12 }}>
              Payload SHA-256: <code>{s.payloadSha256Hex}</code>
            </div>
          </li>
        ))}
      </ol>
    </div>
  )
}
