import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiPostBareCreated, apiPostBareOk } from '../api/client'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Step = {
  id: string
  stepIndex: number
  itemStatus: number
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

export function DownloadDetail() {
  const { id } = useParams()
  const [d, setD] = useState<Detail | null>(null)
  const [err, setErr] = useState<string | null>(null)

  const load = () => {
    if (!id) return
    setErr(null)
    fetch(`/api/downloads/${id}`, { headers })
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
      const j = await apiPostBareCreated<{ id: string }>(`/api/downloads/${id}/retry`, { confirm: true })
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

  const canCancel = d.status === 1 || d.status === 5
  const canRetry = d.status === 3 || d.status === 4

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
      <p>
        Set: {d.tableSetName ?? d.tableSetId} · Scope: {d.scope === 1 ? 'Partial' : 'Full'} · Retries: {d.retryCount}
      </p>
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
              Status: {s.lastAckStatus} · Item: {s.itemStatus}
            </div>
            <div style={{ fontSize: 12 }}>
              Payload SHA-256: <code>{s.payloadSha256Hex}</code>
            </div>
          </li>
        ))}
      </ol>
    </div>
  )
}
