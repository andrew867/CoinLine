import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { apiPost } from '../api/client'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Batch = {
  id: string
  terminalId: string
  tableSetId: string
  status: number
  scope: number
  retryCount: number
  completedAtUtc: string | null
  lastError: string | null
}

type Term = { id: string; displayName: string }
type SetRow = { id: string; name: string; status: number }

export function DownloadsPage() {
  const [search] = useSearchParams()
  const preTerm = search.get('terminal') ?? ''
  const [rows, setRows] = useState<Batch[] | null>(null)
  const [terms, setTerms] = useState<Term[]>([])
  const [sets, setSets] = useState<SetRow[]>([])
  const [terminalId, setTerminalId] = useState(preTerm)
  const [tableSetId, setTableSetId] = useState('')
  const [err, setErr] = useState<string | null>(null)

  const load = () => {
    setErr(null)
    fetch('/api/downloads', { headers })
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<Batch[]>
      })
      .then(setRows)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
  }

  useEffect(() => {
    load()
  }, [])

  useEffect(() => {
    let c = false
    async function safeJson<T>(path: string): Promise<T | null> {
      try {
        const r = await fetch(path, { headers })
        if (!r.ok) return null
        const ct = r.headers.get('content-type')
        if (!ct?.includes('application/json')) return null
        return (await r.json()) as T
      } catch {
        return null
      }
    }
    void safeJson<Term[]>('/api/terminals').then((t) => {
      if (!c && t) setTerms(t)
    })
    void safeJson<SetRow[]>('/api/tables/sets').then((s) => {
      if (!c && s) {
        setSets(s)
        const pub = s.find((x) => x.status === 1)
        if (pub && !tableSetId) setTableSetId(pub.id)
      }
    })
    return () => {
      c = true
    }
  }, [])

  useEffect(() => {
    if (preTerm) setTerminalId(preTerm)
  }, [preTerm])

  async function startDownload(e: React.FormEvent) {
    e.preventDefault()
    setErr(null)
    try {
      await apiPost<{ id: string }>('/api/terminals/' + terminalId + '/downloads', {
        tableSetId,
        scope: 'Full',
      })
      load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

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

  return (
    <div>
      <h1>Download batches</h1>
      <p style={{ color: '#555' }}>
        Batches stay in <strong>Running</strong> until terminal hardware ACKs exist (not implemented in this tranche). Use cancel to stop
        orchestration.
      </p>
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      {terms.length === 0 && (
        <p style={{ color: '#666' }}>
          No terminals yet — create one under <Link to="/terminals">Terminals</Link>.
        </p>
      )}
      {sets.filter((s) => s.status === 1).length === 0 && (
        <p style={{ color: '#a50' }}>No published table sets. Draft sets must be published (API/UI with confirm) before download.</p>
      )}

      <h2>Start download</h2>
      <form onSubmit={startDownload} style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'flex-end', marginBottom: 24 }}>
        <label>
          Terminal
          <select
            required
            value={terminalId}
            onChange={(e) => setTerminalId(e.target.value)}
            style={{ display: 'block', minWidth: 200 }}
          >
            <option value="">—</option>
            {terms.map((t) => (
              <option key={t.id} value={t.id}>
                {t.displayName}
              </option>
            ))}
          </select>
        </label>
        <label>
          Published table set
          <select
            required
            value={tableSetId}
            onChange={(e) => setTableSetId(e.target.value)}
            style={{ display: 'block', minWidth: 200 }}
          >
            {sets
              .filter((s) => s.status === 1)
              .map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
          </select>
        </label>
        <button type="submit">Create batch</button>
      </form>

      <h2>Recent batches</h2>
      {!rows && <p>Loading…</p>}
      {rows && rows.length === 0 && <p style={{ color: '#666' }}>No download batches yet.</p>}
      {rows && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
              <th>Id</th>
              <th>Status</th>
              <th>Scope</th>
              <th>Terminal</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((b) => (
              <tr key={b.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>
                  <Link to={`/downloads/${b.id}`}>{b.id.slice(0, 8)}…</Link>
                </td>
                <td>{statusLabel(b.status)}</td>
                <td>{b.scope === 1 ? 'Partial' : 'Full'}</td>
                <td>
                  <code>{b.terminalId.slice(0, 8)}…</code>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
