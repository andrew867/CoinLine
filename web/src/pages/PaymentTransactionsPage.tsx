import { useEffect, useState } from 'react'
import { apiGet } from '../api/client'

type Row = {
  id: string
  cardAccountId: string
  amount: number
  reconciliation: number
  reportedCardType: number
  createdAtUtc: string
}

export function PaymentTransactionsPage() {
  const [rows, setRows] = useState<Row[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    setLoading(true)
    setErr(null)
    apiGet<Row[]>('/api/cards/transactions')
      .then(setRows)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
      .finally(() => setLoading(false))
  }, [])

  return (
    <div>
      <h1>Payment transactions</h1>
      <p style={{ color: '#555', maxWidth: 720 }}>
        Raw payloads are stored server-side for unknown rails — list view shows summary fields only.
      </p>
      {loading && <p role="status">Loading…</p>}
      {!loading && err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!loading && !err && rows.length === 0 && (
        <p role="status">No payment transactions recorded.</p>
      )}
      {!loading && !err && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ borderBottom: '1px solid #ccc' }}>
              <th align="left">Created</th>
              <th align="left">Account</th>
              <th align="right">Amount</th>
              <th align="left">Card type</th>
              <th align="left">Reconciliation</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>{new Date(r.createdAtUtc).toLocaleString()}</td>
                <td>{r.cardAccountId}</td>
                <td align="right">{r.amount}</td>
                <td>{r.reportedCardType}</td>
                <td>{r.reconciliation}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
