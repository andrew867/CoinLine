import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiFetch, apiPost } from '../api/client'
import { dispositionWords, reconciliationWords } from '../callWords'


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

function modeWords(mode: number): string {
  return ({ 0: 'Unknown', 1: 'Real-time', 2: 'Set rated', 3: 'Table rated' } as Record<number, string>)[mode] ?? 'Unknown'
}

export function CallRecordDetail() {
  const { id } = useParams()
  const [d, setD] = useState<Doc | null>(null)
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)
  const [actionErr, setActionErr] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const load = async (): Promise<Doc | null> => {
    if (!id) return null
    const r = await apiFetch(`/api/call-records/${id}`)
    if (r.status === 404) return null
    if (!r.ok) throw new Error(String(r.status))
    return (await r.json()) as Doc
  }

  useEffect(() => {
    if (!id) return
    setLoading(true)
    setErr(null)
    load()
      .then(setD)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
      .finally(() => setLoading(false))
  }, [id])

  async function mark(status: number, note: string) {
    if (!id) return
    setBusy(true)
    setActionErr(null)
    try {
      await apiPost(`/api/call-records/${id}/reconcile`, { status, note, confirm: true })
      setD(await load())
    } catch (ex) {
      setActionErr(ex instanceof Error ? ex.message : 'error')
    } finally {
      setBusy(false)
    }
  }

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
            <strong>Dialed:</strong> {d.dialedDigits} · <strong>How priced:</strong> {modeWords(d.mode)} · <strong>Disposition:</strong>{' '}
            {dispositionWords(d.disposition)} · <strong>Books:</strong> {reconciliationWords(d.reconciliation)}
          </p>
          <div className="row-actions">
            <button type="button" disabled={busy || d.reconciliation === 2} onClick={() => void mark(2, 'Matched from the console')}>
              Mark matched
            </button>
            <button type="button" className="quiet" disabled={busy || d.reconciliation === 3} onClick={() => void mark(3, 'Exception from the console')}>
              Mark exception
            </button>
          </div>
          {actionErr && <p style={{ color: 'crimson' }}>{actionErr}</p>}
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
