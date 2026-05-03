import type { FormEvent } from 'react'
import { useEffect, useState } from 'react'
import { EmptyHint, ErrorBanner, LoadingBlock } from '../components/AppStates'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type SiteRow = { id: string; customerId: string; name: string; code: string }
type Cust = { id: string; name: string; code: string }

export function SitesPage() {
  const [sites, setSites] = useState<SiteRow[] | null>(null)
  const [customers, setCustomers] = useState<Cust[]>([])
  const [customerId, setCustomerId] = useState('')
  const [name, setName] = useState('')
  const [code, setCode] = useState('')
  const [err, setErr] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    void fetch('/api/customers', { headers })
      .then((r) => r.json() as Promise<Cust[]>)
      .then((c) => {
        setCustomers(c)
        setCustomerId((prev) => prev || (c[0]?.id ?? ''))
      })
      .catch(() => {})
    void fetch('/api/sites', { headers })
      .then((r) => {
        if (!r.ok) throw new Error(String(r.status))
        return r.json() as Promise<SiteRow[]>
      })
      .then(setSites)
      .catch((e) => setErr(e instanceof Error ? e.message : 'error'))
  }, [])

  async function onCreate(e: FormEvent) {
    e.preventDefault()
    if (!customerId) return
    setSaving(true)
    setErr(null)
    try {
      const r = await fetch('/api/sites', {
        method: 'POST',
        headers: { ...headers, 'Content-Type': 'application/json' },
        body: JSON.stringify({ customerId, name: name.trim(), code: code.trim() }),
      })
      if (!r.ok) throw new Error(`${r.status}`)
      setName('')
      setCode('')
      const list = await fetch('/api/sites', { headers }).then((x) => x.json() as Promise<SiteRow[]>)
      setSites(list)
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'create failed')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div>
      <h1>Sites</h1>
      <p style={{ color: '#555', maxWidth: 720 }}>Sites belong to a customer and host terminals.</p>
      {err && <ErrorBanner message={err} />}
      <form
        onSubmit={(e) => void onCreate(e)}
        style={{ display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'flex-end', marginBottom: 16 }}
      >
        <label>
          Customer
          <select
            data-testid="site-customer"
            value={customerId}
            onChange={(e) => setCustomerId(e.target.value)}
            style={{ display: 'block', minWidth: 220 }}
          >
            {customers.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name} ({c.code})
              </option>
            ))}
          </select>
        </label>
        <label>
          Site name
          <input
            data-testid="site-name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            required
            style={{ display: 'block', minWidth: 200 }}
          />
        </label>
        <label>
          Code
          <input
            data-testid="site-code"
            value={code}
            onChange={(e) => setCode(e.target.value)}
            required
            style={{ display: 'block', minWidth: 100 }}
          />
        </label>
        <button type="submit" data-testid="site-create" disabled={saving}>
          {saving ? 'Saving…' : 'Create site'}
        </button>
      </form>
      {sites === null && <LoadingBlock />}
      {sites && sites.length === 0 && <EmptyHint>No sites yet.</EmptyHint>}
      {sites && sites.length > 0 && (
        <table cellPadding={8} style={{ borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ borderBottom: '1px solid #ccc', textAlign: 'left' }}>
              <th>Name</th>
              <th>Code</th>
              <th>Customer id</th>
            </tr>
          </thead>
          <tbody>
            {sites.map((s) => (
              <tr key={s.id} style={{ borderBottom: '1px solid #eee' }}>
                <td>{s.name}</td>
                <td>
                  <code>{s.code}</code>
                </td>
                <td>
                  <code style={{ fontSize: 12 }}>{s.customerId}</code>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}
