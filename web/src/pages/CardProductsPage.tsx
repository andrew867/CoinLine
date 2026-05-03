import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { apiGet, apiPost } from '../api/client'

type Row = {
  id: string
  name: string
  code: string
  defaultCardType: number
  allowNegativeBalance: boolean
  isTestFixtureCatalogEntry: boolean
}

export function CardProductsPage() {
  const [rows, setRows] = useState<Row[]>([])
  const [loading, setLoading] = useState(true)
  const [err, setErr] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [code, setCode] = useState('')
  const [defaultCardType, setDefaultCardType] = useState(1)
  const [saving, setSaving] = useState(false)

  const load = () =>
    apiGet<Row[]>('/api/cards/products')
      .then(setRows)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))

  useEffect(() => {
    setLoading(true)
    setErr(null)
    load().finally(() => setLoading(false))
  }, [])

  async function onCreate(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setErr(null)
    try {
      await apiPost<{ id: string }>('/api/cards/products', {
        name,
        code,
        defaultCardType,
        allowNegativeBalance: false,
        isTestFixtureCatalogEntry: false,
      })
      setName('')
      setCode('')
      await load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'create failed')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div>
      <h1>Card products</h1>
      <p style={{ color: '#555', maxWidth: 720 }}>
        Maps toward MT 134 / SC_PARM-style catalogs — full issuer semantics remain{' '}
        <code>HARDWARE_VALIDATION_REQUIRED</code>.
      </p>
      {loading && <p role="status">Loading…</p>}
      {!loading && err && <p style={{ color: 'crimson' }}>{err}</p>}
      {!loading && !err && rows.length === 0 && (
        <p role="status">No card products. Create one below or load seed data.</p>
      )}
      {!loading && !err && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse', marginBottom: 24 }}>
          <thead>
            <tr style={{ borderBottom: '1px solid #ccc' }}>
              <th align="left">Name</th>
              <th align="left">Code</th>
              <th align="left">Default type</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>
                  <Link to={`/card-products/${r.id}`}>{r.name}</Link>
                </td>
                <td>{r.code}</td>
                <td>{r.defaultCardType}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      <h2 style={{ fontSize: '1.1rem' }}>Create product</h2>
      <form onSubmit={onCreate} style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'flex-end' }}>
        <label>
          Name
          <input value={name} onChange={(e) => setName(e.target.value)} required style={{ display: 'block' }} />
        </label>
        <label>
          Code
          <input value={code} onChange={(e) => setCode(e.target.value)} required style={{ display: 'block' }} />
        </label>
        <label>
          Default card type (enum int)
          <input
            type="number"
            min={0}
            max={6}
            value={defaultCardType}
            onChange={(e) => setDefaultCardType(Number(e.target.value))}
            style={{ display: 'block', width: 80 }}
          />
        </label>
        <button type="submit" disabled={saving}>
          {saving ? 'Saving…' : 'Create'}
        </button>
      </form>
    </div>
  )
}
