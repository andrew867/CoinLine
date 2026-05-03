import { useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { apiPostBareOk, apiPut } from '../api/client'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Ver = {
  id: string
  tableDefinitionId: string
  definitionName: string
  tableRevision: number
  payloadSha256Hex: string
  payloadLengthBytes: number
  sortOrder: number
  validationPassed: boolean
  validationDiagnosticsJson: string | null
  warnings: string[]
}

type Detail = {
  id: string
  name: string
  customerId: string | null
  isDefault: boolean
  status: number
  publishedAtUtc: string | null
  publishGeneration: number
  versions: Ver[]
}

export function TableSetDetail() {
  const { id } = useParams()
  const [d, setD] = useState<Detail | null>(null)
  const [err, setErr] = useState<string | null>(null)
  const [name, setName] = useState('')

  const load = () => {
    if (!id) return
    setErr(null)
    fetch(`/api/tables/sets/${id}`, { headers })
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<Detail>
      })
      .then((j) => {
        setD(j)
        setName(j.name)
      })
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
  }

  useEffect(() => {
    load()
  }, [id])

  async function save(e: React.FormEvent) {
    e.preventDefault()
    if (!id) return
    setErr(null)
    try {
      await apiPut(`/api/tables/sets/${id}`, {
        name,
        customerId: d?.customerId ?? null,
        isDefault: d?.isDefault ?? false,
      })
      load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  async function publish() {
    if (!id) return
    if (
      !window.confirm(
        'Publish this table set? Published sets affect which payloads terminals may download (validate on bench — HARDWARE_VALIDATION_REQUIRED).'
      )
    )
      return
    setErr(null)
    try {
      await apiPostBareOk(`/api/tables/sets/${id}/publish`, { confirm: true })
      load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'error')
    }
  }

  if (!id) return <p>Missing id.</p>
  if (!d) return err ? <p style={{ color: 'crimson' }}>{err}</p> : <p>Loading…</p>

  const draft = d.status !== 1
  const noVersions = d.versions.length === 0

  return (
    <div>
      <p>
        <Link to="/table-sets">← Table sets</Link>
      </p>
      <h1>Table set: {d.name}</h1>
      {err && <p style={{ color: 'crimson' }}>Action failed: {err}</p>}
      <p>
        Status: <strong>{draft ? 'Draft' : 'Published'}</strong>
        {d.publishedAtUtc && (
          <>
            {' '}
            at {new Date(d.publishedAtUtc).toLocaleString()} (gen {d.publishGeneration})
          </>
        )}
      </p>
      {draft && (
        <form onSubmit={save} style={{ marginBottom: 16, display: 'flex', gap: 8, alignItems: 'center' }}>
          <label>
            Name{' '}
            <input value={name} onChange={(e) => setName(e.target.value)} />
          </label>
          <button type="submit">Save</button>
          <button type="button" onClick={() => void publish()}>
            Publish set
          </button>
        </form>
      )}
      <p>
        <Link to={`/table-versions?set=${encodeURIComponent(id)}`}>View versions list</Link>
      </p>
      <h2>Versions in this set</h2>
      {noVersions && (
        <p style={{ color: '#666' }}>No versions yet — POST a table version via API or add seed data.</p>
      )}
      <table cellPadding={8} style={{ borderCollapse: 'collapse', fontSize: 14 }}>
        <thead>
          <tr style={{ textAlign: 'left', borderBottom: '1px solid #ccc' }}>
            <th>Table</th>
            <th>Rev</th>
            <th>SHA-256 (payload)</th>
            <th>Bytes</th>
            <th>Valid</th>
            <th>Warnings</th>
          </tr>
        </thead>
        <tbody>
          {d.versions.map((v) => (
            <tr key={v.id} style={{ borderBottom: '1px solid #eee', verticalAlign: 'top' }}>
              <td>{v.definitionName}</td>
              <td>{v.tableRevision}</td>
              <td>
                <code style={{ fontSize: 11 }} title="SHA-256 of opaque table payload bytes">
                  {v.payloadSha256Hex}
                </code>
              </td>
              <td>{v.payloadLengthBytes}</td>
              <td>{v.validationPassed ? 'yes' : 'no'}</td>
              <td>
                {v.warnings?.length ? (
                  <ul style={{ margin: 0, paddingLeft: 18, color: '#a50' }}>
                    {v.warnings.map((w) => (
                      <li key={w}>{w}</li>
                    ))}
                  </ul>
                ) : (
                  ''
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
