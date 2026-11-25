import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiGet, apiPost } from '../api/client'

type Row = {
  id: string
  status: number
  createdAtUtc: string
  postedAtUtc: string | null
  closedAtUtc: string | null
  paymentCount: number
  callCount: number
  paymentTotal: number
  callTotal: number
}

type Pending = { paymentCount: number; paymentTotal: number; callCount: number }

const batchWords: Record<number, string> = {
  0: 'Open',
  1: 'Posted',
  2: 'Closed',
  3: 'Exception',
}

export function ReconciliationBatchesPage() {
  const [rows, setRows] = useState<Row[] | null>(null)
  const [pending, setPending] = useState<Pending | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const load = () => {
    setErr(null)
    return Promise.all([
      apiGet<Row[]>('/api/cards/reconciliation-batches').then(setRows),
      apiGet<Pending>('/api/cards/reconciliation-pending').then(setPending),
    ]).catch((e) => setErr(e instanceof Error ? e.message : 'error'))
  }

  useEffect(() => {
    void load()
  }, [])

  async function openBatch() {
    setBusy(true)
    setErr(null)
    try {
      const created = await apiPost<{ id: string }>('/api/cards/reconciliation-batches', {
        detailJson: '{"source":"console"}',
      })
      window.location.assign(`/card-reconciliation/${created.id}`)
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
      setBusy(false)
    }
  }

  return (
    <div>
      <h1>Reconciliation</h1>
      <p className="page-intro">
        Open a batch, then post it. Posting matches every card payment and call that is still waiting, and writes the
        totals onto the batch.
      </p>
      {pending && (
        <p>
          Waiting now: <strong>{pending.callCount}</strong> calls and <strong>{pending.paymentCount}</strong> card
          payments ({money(pending.paymentTotal)}).
        </p>
      )}
      <button type="button" disabled={busy} onClick={() => void openBatch()}>
        {busy ? 'Opening…' : 'Open a batch'}
      </button>
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!rows && !err && <p role="status">Loading…</p>}
      {rows && rows.length === 0 && <p role="status">No batches yet.</p>}
      {rows && rows.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Opened</th>
              <th>Status</th>
              <th>Calls</th>
              <th>Card payments</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id}>
                <td>
                  <Link to={`/card-reconciliation/${r.id}`}>{new Date(r.createdAtUtc).toLocaleString()}</Link>
                </td>
                <td>{batchWords[r.status] ?? 'Unknown'}</td>
                <td>{r.callCount}</td>
                <td>
                  {r.paymentCount}
                  {r.paymentCount ? ` · ${money(r.paymentTotal)}` : ''}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}

function money(amount: number): string {
  return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' }).format(amount)
}
