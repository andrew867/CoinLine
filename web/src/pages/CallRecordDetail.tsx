import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Diag = { code: string; severity: string; message: string }
type Res = {
  id: string
  amount: number
  decisionKind: number
  determinismInputJson: string
  diagnostics: Diag[]
}

type Doc = {
  id: string
  dialedDigits: string
  mode: number
  disposition: number
  reconciliation: number
  results: Res[]
}

export function CallRecordDetail() {
  const { id } = useParams()
  const [d, setD] = useState<Doc | null>(null)
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    if (!id) return
    setLoading(true)
    setErr(null)
    fetch(`/api/call-records/${id}`, { headers })
      .then((r) => {
        if (r.status === 404) return null
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<Doc>
      })
      .then(setD)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
      .finally(() => setLoading(false))
  }, [id])

  if (!id) return null

  return (
    <div>
      <p>
        <Link to="/call-records">← Call records</Link>
      </p>
      <h1>Call record</h1>
      {loading && <p role="status">Loading…</p>}
      {!loading && err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!loading && !err && !d && <p role="status">Not found.</p>}
      {!loading && !err && d && (
        <>
          <p>
            <strong>Dialed:</strong> {d.dialedDigits} · <strong>Mode:</strong> {d.mode} · <strong>Disposition:</strong>{' '}
            {d.disposition} · <strong>Reconciliation:</strong> {d.reconciliation}
          </p>
          <h2>Rating diagnostics</h2>
          {d.results.length === 0 && <p role="status">No rating results attached.</p>}
          {d.results.map((r) => (
            <div key={r.id} style={{ marginBottom: 16 }}>
              <p>
                Result {r.id.slice(0, 8)}… · amount {r.amount} · decisionKind {r.decisionKind}
              </p>
              {r.diagnostics.length === 0 && <p>No diagnostics.</p>}
              <ul>
                {r.diagnostics.map((x) => (
                  <li key={`${x.code}-${x.message}`}>
                    <strong>{x.code}</strong> ({x.severity}): {x.message}
                  </li>
                ))}
              </ul>
              <details>
                <summary>Determinism input JSON</summary>
                <pre style={{ background: '#f6f6f6', padding: 8 }}>{r.determinismInputJson}</pre>
              </details>
            </div>
          ))}
        </>
      )}
    </div>
  )
}
