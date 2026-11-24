import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { terminalStatusLabel } from '../components/StatusBadge'
import { apiFetch } from '../api/client'
import { TerminalFamilySelect } from '../components/TerminalFamilySelect'


type Row = {
  id: string
  siteId: string
  displayName: string
  terminalIdHex: string
  status: number
  groupName: string | null
  terminalFamilyLabel: string | null
  regionCode: string | null
  onCall: boolean
  lastSessionAtUtc: string | null
}

export function TerminalsList() {
  const [rows, setRows] = useState<Row[] | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [family, setFamily] = useState('')
  const [search, setSearch] = useState('')
  const [region, setRegion] = useState('')
  const [filters, setFilters] = useState({ search: '', region: '' })

  useEffect(() => {
    let c = false
    const query = new URLSearchParams()
    if (family) query.set('terminalFamilyId', family)
    if (filters.search) query.set('q', filters.search)
    if (filters.region) query.set('regionCode', filters.region)
    apiFetch('/api/terminals?' + query)
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<Row[]>
      })
      .then((j) => {
        if (!c) setRows(j)
      })
      .catch((e) => {
        if (!c) setErr(e instanceof Error ? e.message : 'error')
      })
    return () => {
      c = true
    }
  }, [family, filters])

  function filterFamily(value: string) {
    if (value === family) return
    setFamily(value)
    setRows(null)
    setErr(null)
  }

  function applySearch(event: FormEvent) {
    event.preventDefault()
    const next = { search: search.trim(), region: region.trim() }
    if (next.search === filters.search && next.region === filters.region) return
    setFilters(next)
    setRows(null)
    setErr(null)
  }

  return (
    <div>
      <h1>Phones</h1>
      <p>
        <Link to="/terminals/new" data-testid="terminals-add-link">
          Add a phone
        </Link>
      </p>
      <form className="table-config-selectors" aria-label="Find phones" onSubmit={applySearch}>
        <label>Name or terminal ID<input value={search} onChange={event => setSearch(event.target.value)} /></label>
        <label>Region code<input value={region} maxLength={32} onChange={event => setRegion(event.target.value)} /></label>
        <TerminalFamilySelect value={family} onChange={filterFamily} emptyLabel="All families" />
        <button type="submit">Apply filters</button>
      </form>
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!rows && <p>Loading…</p>}
      {rows && rows.length === 0 && <p>{family || filters.search || filters.region ? 'No matching phones.' : 'No terminals yet.'}</p>}
      {rows && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
              <th>Name</th>
              <th>Hex id</th>
              <th>Region</th>
              <th>Group</th>
              <th>Family</th>
              <th>Status</th>
              <th>Last call</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>
                  <Link to={`/terminals/${r.id}`}>{r.displayName}</Link>
                </td>
                <td>
                  <code>{r.terminalIdHex}</code>
                </td>
                <td>{r.regionCode ?? '—'}</td>
                <td>{r.groupName ?? '—'}</td>
                <td>{r.terminalFamilyLabel ?? 'Unknown'}</td>
                <td>{terminalStatusLabel(r.status)}</td>
                <td>{r.onCall ? 'On a call' : r.lastSessionAtUtc ? new Date(r.lastSessionAtUtc).toLocaleString() : '—'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
