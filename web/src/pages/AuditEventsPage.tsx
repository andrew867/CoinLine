import type { FormEvent } from 'react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { EmptyHint, ErrorBanner, LoadingBlock } from '../components/AppStates'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Row = {
  id: string
  category: string
  action: string
  actor: string
  resource: string
  detailJson: string
  correlationId: string | null
  terminalId: string | null
  createdAtUtc: string
}

type Paged = { total: number; page: number; pageSize: number; items: Row[] }

export function AuditEventsPage() {
  const [loading, setLoading] = useState(true)
  const [rows, setRows] = useState<Row[] | null>(null)
  const [total, setTotal] = useState<number | null>(null)
  const [page, setPage] = useState(1)
  const pageSize = 25
  const [category, setCategory] = useState('')
  const [q, setQ] = useState('')
  const [debouncedQ, setDebouncedQ] = useState('')
  const [terminalFilter, setTerminalFilter] = useState('')
  const [err, setErr] = useState<string | null>(null)
  const [reloadKey, setReloadKey] = useState(0)

  useEffect(() => {
    const t = window.setTimeout(() => setDebouncedQ(q.trim()), 350)
    return () => window.clearTimeout(t)
  }, [q])

  useEffect(() => {
    setLoading(true)
    setErr(null)
    const params = new URLSearchParams()
    params.set('page', String(page))
    params.set('pageSize', String(pageSize))
    if (category.trim()) params.set('category', category.trim())
    if (debouncedQ) params.set('q', debouncedQ)
    if (terminalFilter.trim()) params.set('terminalId', terminalFilter.trim())
    let cancelled = false
    void fetch(`/api/audit/events?${params}`, { headers })
      .then(async (r) => {
        if (!r.ok) throw new Error(String(r.status))
        const j: unknown = await r.json()
        if (cancelled) return
        if (Array.isArray(j)) {
          setRows(j as Row[])
          setTotal(null)
        } else {
          const p = j as Paged
          setRows(p.items)
          setTotal(p.total)
        }
      })
      .catch((e) => {
        if (!cancelled) setErr(e instanceof Error ? e.message : 'error')
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [page, category, debouncedQ, terminalFilter, reloadKey])

  function onFilter(e: FormEvent) {
    e.preventDefault()
    setPage(1)
    setReloadKey((k) => k + 1)
  }

  return (
    <div>
      <h1>Audit events</h1>
      <p style={{ color: '#555', maxWidth: 720 }}>
        Search operator-visible audit trail — JSON detail is shown verbatim (including unknown keys from integrations).
      </p>
      <form onSubmit={onFilter} style={{ display: 'flex', flexWrap: 'wrap', gap: 8, marginBottom: 12 }}>
        <label>
          Category
          <input value={category} onChange={(e) => setCategory(e.target.value)} style={{ display: 'block' }} />
        </label>
        <label>
          Text filter
          <input
            data-testid="audit-search-q"
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder="actor, resource, detail…"
            style={{ display: 'block', minWidth: 200 }}
          />
        </label>
        <label>
          Terminal id
          <input value={terminalFilter} onChange={(e) => setTerminalFilter(e.target.value)} style={{ display: 'block' }} />
        </label>
        <button type="submit">Apply</button>
      </form>
      {err && <ErrorBanner message={err} />}
      {loading && <LoadingBlock />}
      {!loading && rows && rows.length === 0 && <EmptyHint>No audit rows for this filter.</EmptyHint>}
      {total !== null && (
        <p style={{ fontSize: 13 }}>
          Total {total} · page {page} ·{' '}
          <button type="button" disabled={page <= 1} onClick={() => setPage((p) => Math.max(1, p - 1))}>
            Prev
          </button>{' '}
          <button type="button" disabled={page * pageSize >= total} onClick={() => setPage((p) => p + 1)}>
            Next
          </button>
        </p>
      )}
      {!loading && rows && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse', fontSize: 13, width: '100%' }}>
          <thead>
            <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
              <th>When</th>
              <th>Category</th>
              <th>Action</th>
              <th>Actor</th>
              <th>Terminal</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee', verticalAlign: 'top' }}>
                <td>{new Date(r.createdAtUtc).toLocaleString()}</td>
                <td>{r.category}</td>
                <td>{r.action}</td>
                <td>{r.actor}</td>
                <td>
                  {r.terminalId ? <Link to={`/terminals/${r.terminalId}`}>{r.terminalId.slice(0, 8)}…</Link> : '—'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {!loading && rows && rows.length > 0 && (
        <>
          <h2>Detail JSON (latest filter match)</h2>
          <pre
            style={{
              background: '#f6f8fa',
              padding: 12,
              overflow: 'auto',
              maxHeight: 240,
              fontSize: 12,
            }}
          >
            {rows[0]?.detailJson}
          </pre>
        </>
      )}
    </div>
  )
}
