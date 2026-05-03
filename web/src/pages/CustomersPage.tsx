import type { FormEvent } from 'react'
import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { EmptyHint, ErrorBanner, LoadingBlock } from '../components/AppStates'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type Row = { id: string; name: string; code: string; createdAtUtc?: string }

export function CustomersPage() {
  const [rows, setRows] = useState<Row[] | null>(null)
  const [name, setName] = useState('')
  const [code, setCode] = useState('')
  const [err, setErr] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  const load = () => {
    setErr(null)
    fetch('/api/customers', { headers })
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<Row[]>
      })
      .then(setRows)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
  }

  useEffect(() => {
    load()
  }, [])

  async function onCreate(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setErr(null)
    try {
      const r = await fetch('/api/customers', {
        method: 'POST',
        headers: { ...headers, 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: name.trim(), code: code.trim() }),
      })
      if (!r.ok) throw new Error(`${r.status}`)
      setName('')
      setCode('')
      load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'create failed')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div>
      <h1>Customers</h1>
      <p style={{ color: '#555', maxWidth: 720 }}>
        Operator-facing directory — links open the customer console (sites, terminals, tables, rating, cards).
      </p>
      {err && <ErrorBanner message={err} />}
      <form
        onSubmit={(e) => void onCreate(e)}
        style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'flex-end', marginBottom: 16 }}
      >
        <label>
          Name
          <input
            data-testid="customer-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
            style={{ display: 'block', minWidth: 200 }}
          />
        </label>
        <label>
          Code
          <input
            data-testid="customer-code"
            value={code}
            onChange={(e) => setCode(e.target.value)}
            required
            style={{ display: 'block', minWidth: 120 }}
          />
        </label>
        <button type="submit" data-testid="customer-create" disabled={saving}>
          {saving ? 'Saving…' : 'Create'}
        </button>
      </form>
      {rows === null && <LoadingBlock />}
      {rows && rows.length === 0 && <EmptyHint>No customers yet — create one above.</EmptyHint>}
      {rows && rows.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ borderBottom: '1px solid #ccc', textAlign: 'left' }}>
              <th>Name</th>
              <th>Code</th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>
                  <Link data-testid={`customer-link-${r.code}`} to={`/customers/${r.id}`}>
                    {r.name}
                  </Link>
                </td>
                <td>
                  <code>{r.code}</code>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
