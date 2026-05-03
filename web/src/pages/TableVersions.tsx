import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Row = {
  id: string
  tableSetId: string
  tableDefinitionId: string
  definitionName: string
  tableRevision: number
  payloadSha256Hex: string
  payloadLengthBytes: number
  sortOrder: number
  validationPassed: boolean
  validationDiagnosticsJson: string | null
}

export function TableVersions() {
  const [search] = useSearchParams()
  const setFilter = search.get('set') ?? ''
  const [rows, setRows] = useState<Row[] | null>(null)
  const [err, setErr] = useState<string | null>(null)

  useEffect(() => {
    let c = false
    const q = setFilter ? `?tableSetId=${encodeURIComponent(setFilter)}` : ''
    setErr(null)
    fetch(`/api/tables/versions${q}`, { headers })
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
  }, [setFilter])

  return (
    <div>
      <h1>Table versions</h1>
      <p style={{ color: '#555' }}>
        Filter by set: add <code>?set=&#123;guid&#125;</code> to the URL, or open from a{' '}
        <Link to="/table-sets">table set</Link>.
      </p>
      {setFilter && (
        <p>
          Set filter: <code>{setFilter}</code>{' '}
          <Link to="/table-versions">clear</Link>
        </p>
      )}
      {err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!rows && <p>Loading…</p>}
      {rows && rows.length === 0 && <p style={{ color: '#666' }}>No table versions match this filter.</p>}
      {rows && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse', fontSize: 14 }}>
          <thead>
            <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
              <th>Definition</th>
              <th>Rev</th>
              <th>SHA-256 (payload)</th>
              <th>Bytes</th>
              <th>OK</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>
                  <Link to={`/table-sets/${r.tableSetId}`}>{r.definitionName}</Link>
                </td>
                <td>{r.tableRevision}</td>
                <td>
                  <code style={{ fontSize: 12 }}>{r.payloadSha256Hex}</code>
                </td>
                <td>{r.payloadLengthBytes}</td>
                <td>{r.validationPassed ? 'yes' : <span style={{ color: 'crimson' }}>no</span>}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
