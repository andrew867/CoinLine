import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiFetch, apiPostBare, apiPostBareOk } from '../api/client'


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
export function DownloadDetail() {
  const { id } = useParams()
  const [d, setD] = useState<Detail | null>(null)
  const [err, setErr] = useState<string | null>(null)

  const load = () => {
    if (!id) return
    setErr(null)
    apiFetch(`/api/downloads/${id}`)
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<Detail>
      })
      .then(setD)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
  }

  useEffect(() => {
    load()
  }, [id])

  async function cancel() {
    if (!id) return
    if (
      !window.confirm(
        'Cancel this download orchestration batch? (Terminal-side state may still require HARDWARE_VALIDATION_REQUIRED checks.)'
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
        'Create a new download batch from this failed/cancelled run? Confirms server-side orchestration only.'
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
  if (!d) return err ? <p style={{ color: 'crimson' }}>{err}</p> : <p>Loading…</p>

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

  const canCancel = !d.scheduleId && (d.status === 0 || (d.status === 1 && !d.nccSessionId) || d.status === 5)
  const canRetry = !d.scheduleId && (d.status === 3 || d.status === 4)

  return (
    <div>
      <p>
        <Link to="/downloads">← Downloads</Link>
      </p>
      <h1>Download batch</h1>
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      <p>
        <strong>{statusLabel(d.status)}</strong> · Terminal <code>{d.terminalId}</code>
      </p>
      {d.nccSessionId && <p>NCC session <code>{d.nccSessionId}</code></p>}
      {d.scheduleId && <p><Link to={`/table-sets/${d.tableSetId}`}>Manage scheduled download</Link></p>}
      <p>
        Set: {d.tableSetName ?? d.tableSetId} · Scope: {d.scope === 1 ? 'Partial' : 'Full'} · Retries: {d.retryCount}
      </p>
      {d.lastError && <p role="alert">{d.lastError}</p>}
      {d.diagnosticsJson && (
        <pre style={{ background: '#f6f8fa', padding: 12, fontSize: 12 }}>{d.diagnosticsJson}</pre>
      )}
      <div style={{ display: 'flex', gap: 8, marginBottom: 16 }}>
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
              Step {s.stepIndex}: <strong>{s.tableDefinitionName}</strong>
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
