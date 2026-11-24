import type { FormEvent } from 'react'
import { useEffect, useState } from 'react'
import { ErrorBanner } from '../components/AppStates'

const headers = { 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' }

type GroupRow = { id: string; name: string; customerId: string | null; terminalCount: number }
type Cust = { id: string; name: string; code: string }

export function TerminalGroupsPage() {
  const [rows, setRows] = useState<GroupRow[] | null>(null)
  const [customers, setCustomers] = useState<Cust[]>([])
  const [customerId, setCustomerId] = useState('')
  const [name, setName] = useState('')
  const [err, setErr] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)

  async function load() {
    const list = await fetch('/api/terminal-groups', { headers })
    if (!list.ok) throw new Error(String(list.status))
    setRows(await list.json() as GroupRow[])
  }

  useEffect(() => {
    void fetch('/api/customers', { headers })
      .then((r) => r.json() as Promise<Cust[]>)
      .then((c) => {
        setCustomers(c)
        setCustomerId((prev) => prev || (c[0]?.id ?? ''))
      })
      .catch(() => {})
    void load().catch((e) => setErr(e instanceof Error ? e.message : 'error'))
  }, [])

  async function onCreate(e: FormEvent) {
    e.preventDefault()
    setSaving(true)
    setErr(null)
    try {
      const r = await fetch('/api/terminal-groups', {
        method: 'POST',
        headers: { ...headers, 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: name.trim(), customerId: customerId || null }),
      })
      if (!r.ok) throw new Error(String(r.status))
      setName('')
      await load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'create failed')
    } finally {
      setSaving(false)
    }
  }

  async function rename(id: string, name: string) {
    setErr(null)
    const r = await fetch(`/api/terminal-groups/${id}`, {
      method: 'PUT',
      headers: { ...headers, 'Content-Type': 'application/json' },
      body: JSON.stringify({ name }),
    })
    if (!r.ok) {
      setErr('Could not rename that group.')
      return
    }
    await load()
  }

  async function remove(id: string) {
    setErr(null)
    const r = await fetch(`/api/terminal-groups/${id}`, { method: 'DELETE', headers })
    if (r.status === 409) {
      setErr('Move the phones out of this group first.')
      return
    }
    if (!r.ok && r.status !== 204) {
      setErr('Could not remove that group.')
      return
    }
    await load()
  }

  const customerName = (id: string | null) => customers.find((c) => c.id === id)?.name ?? '—'

  return (
    <div>
      <h1>Phone groups</h1>
      <p>A group is a named set of phones, such as one route or one shelter.</p>
      {err && <ErrorBanner message={err} />}
      <form onSubmit={(e) => void onCreate(e)} className="row-actions">
        <label>
          Name
          <input value={name} onChange={(e) => setName(e.target.value)} required minLength={2} />
        </label>
        <label>
          Customer
          <select value={customerId} onChange={(e) => setCustomerId(e.target.value)}>
            {customers.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name}
              </option>
            ))}
          </select>
        </label>
        <button type="submit" disabled={saving}>
          {saving ? 'Saving…' : 'Add group'}
        </button>
      </form>
      {!rows && <p>Loading…</p>}
      {rows && rows.length === 0 && <p>No groups yet.</p>}
      {rows && rows.length > 0 && (
        <table>
          <thead>
            <tr>
              <th>Name</th>
              <th>Customer</th>
              <th>Phones</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {rows.map((g) => (
              <GroupEditor
                key={g.id}
                group={g}
                customer={customerName(g.customerId)}
                onRename={(name) => void rename(g.id, name)}
                onRemove={() => void remove(g.id)}
              />
            ))}
          </tbody>
        </table>
      )}
    </div>
  )
}

function GroupEditor({
  group,
  customer,
  onRename,
  onRemove,
}: {
  group: GroupRow
  customer: string
  onRename: (name: string) => void
  onRemove: () => void
}) {
  const [name, setName] = useState(group.name)
  return (
    <tr>
      <td>
        <input value={name} onChange={(e) => setName(e.target.value)} aria-label={`Name for ${group.name}`} />
      </td>
      <td>{customer}</td>
      <td>{group.terminalCount}</td>
      <td className="row-actions">
        <button type="button" className="quiet" onClick={() => onRename(name.trim())}>
          Save name
        </button>
        <button type="button" className="quiet" onClick={onRemove}>
          Remove
        </button>
      </td>
    </tr>
  )
}
