import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiGet, apiPost } from '../api/client'

type Detail = {
  id: string
  status: number
  detailJson: string
  postedAtUtc: string | null
  closedAtUtc: string | null
  createdAtUtc: string
}

export function ReconciliationBatchDetail() {
  const { id } = useParams<{ id: string }>()
  const [row, setRow] = useState<Detail | null>(null)
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)
  const [actionErr, setActionErr] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const load = () => {
    if (!id) return Promise.resolve()
    return apiGet<Detail>(`/api/cards/reconciliation-batches/${id}`).then(setRow)
  }

  useEffect(() => {
    if (!id) {
      setLoading(false)
      setErr('Missing batch id.')
      return
    }
    setLoading(true)
    setErr(null)
    load()
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
      .finally(() => setLoading(false))
  }, [id])

  async function postBatch() {
    if (!id) return
    setBusy(true)
    setActionErr(null)
    try {
      await apiPost(`/api/cards/reconciliation-batches/${id}/post`, {})
      await load()
    } catch (ex) {
      setActionErr(ex instanceof Error ? ex.message : 'failed')
    } finally {
      setBusy(false)
    }
  }

  async function closeBatch() {
    if (!id) return
    if (!window.confirm('Close this reconciliation batch? This confirms settlement complete.')) return
    setBusy(true)
    setActionErr(null)
    try {
      await apiPost(`/api/cards/reconciliation-batches/${id}/close`, { confirm: true })
      await load()
    } catch (ex) {
      setActionErr(ex instanceof Error ? ex.message : 'failed')
    } finally {
      setBusy(false)
    }
  }

  async function exceptionBatch() {
    if (!id) return
    if (
      !window.confirm(
        'Mark this batch as Exception? Use when reconciliation cannot be settled — audited on server.',
      )
    )
      return
    setBusy(true)
    setActionErr(null)
    try {
      await apiPost(`/api/cards/reconciliation-batches/${id}/exception`, {
        confirm: true,
        note: 'Marked exception from operator UI',
      })
      await load()
    } catch (ex) {
      setActionErr(ex instanceof Error ? ex.message : 'failed')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div>
      <p>
        <Link to="/card-reconciliation">← Reconciliation batches</Link>
      </p>
      <h1>Reconciliation batch</h1>
      {loading && <p role="status">Loading…</p>}
      {!loading && err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!loading && !err && !row && <p role="status">Not found.</p>}
      {!loading && !err && row && (
        <>
          <p>
            Status: <strong>{row.status}</strong> (0 Open, 1 Posted, 2 Closed, 3 Exception)
          </p>
          <pre style={{ background: '#f6f6f6', padding: 12, fontSize: 12 }}>{row.detailJson}</pre>
          {actionErr && <p style={{ color: 'crimson' }}>{actionErr}</p>}
          <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, marginTop: 12 }}>
            <button type="button" disabled={busy || row.status !== 0} onClick={postBatch}>
              Post batch
            </button>
            <button
              type="button"
              disabled={busy || row.status === 2 || row.status === 3}
              onClick={exceptionBatch}
            >
              Mark exception (confirm)
            </button>
            <button type="button" disabled={busy || row.status !== 1} onClick={closeBatch}>
              Close batch (confirmed)
            </button>
          </div>
        </>
      )}
    </div>
  )
}
