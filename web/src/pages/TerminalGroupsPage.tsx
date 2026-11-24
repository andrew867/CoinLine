import type { FormEvent } from 'react'
import { useEffect, useState } from 'react'
import { ErrorBanner } from '../components/AppStates'
import { apiFetch } from '../api/client'
import { GroupPhones } from './GroupPhones'

type GroupFields = { name: string; code: string | null; description: string | null }
type GroupRow = GroupFields & { id: string; customerId: string | null; terminalCount: number; revision: number }
type Cust = { id: string; name: string; code: string }

async function saveGroup(path: string, method: 'POST' | 'PUT', details: GroupFields & { customerId?: string | null; revision?: number }) {
  const response = await apiFetch(path, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(details),
  })
  if (!response.ok) throw new Error((await response.json().catch(() => null))?.error ?? 'Could not save the group.')
}

export function TerminalGroupsPage() {
  const [rows, setRows] = useState<GroupRow[] | null>(null)
  const [customers, setCustomers] = useState<Cust[]>([])
  const [customerId, setCustomerId] = useState('')
  const [name, setName] = useState('')
  const [code, setCode] = useState('')
  const [description, setDescription] = useState('')
  const [err, setErr] = useState<string | null>(null)
  const [saving, setSaving] = useState(false)
  const [managing, setManaging] = useState<string | null>(null)

  async function load() {
    const list = await apiFetch('/api/terminal-groups')
    if (!list.ok) throw new Error(String(list.status))
    setRows(await list.json() as GroupRow[])
  }

  useEffect(() => {
    void apiFetch('/api/customers')
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
      await saveGroup('/api/terminal-groups', 'POST',
        { name, code, description, customerId: customerId || null })
      setName('')
      setCode('')
      setDescription('')
      await load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'create failed')
    } finally {
      setSaving(false)
    }
  }

  async function update(group: GroupRow, details: GroupFields) {
    setErr(null)
    try {
      await saveGroup(`/api/terminal-groups/${group.id}/details`, 'PUT', { ...details, revision: group.revision })
      await load()
    } catch (ex) {
      setErr(ex instanceof Error ? ex.message : 'Could not save the group.')
    }
  }

  async function remove(id: string) {
    setErr(null)
    const r = await apiFetch(`/api/terminal-groups/${id}`, { method: 'DELETE' })
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
  const managedGroup = rows?.find(group => group.id === managing)

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
        <label>Group ID
          <input value={code} onChange={e => setCode(e.target.value)} maxLength={6} pattern="[A-Za-z0-9]{3,6}" />
        </label>
        <label>Description
          <input value={description} onChange={e => setDescription(e.target.value)} />
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
              <th>Group ID</th>
              <th>Description</th>
              <th>Customer</th>
              <th>Phones</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {rows.map((g) => (
              <GroupEditor
                key={`${g.id}-${g.revision}`}
                group={g}
                customer={customerName(g.customerId)}
                onSave={(details) => update(g, details)}
                onRemove={() => void remove(g.id)}
                onManage={() => setManaging(g.id)}
              />
            ))}
          </tbody>
        </table>
      )}
      {managedGroup && <GroupPhones key={managedGroup.id} group={managedGroup} groups={rows ?? []} onSaved={load} />}
    </div>
  )
}

function GroupEditor({
  group,
  customer,
  onSave,
  onRemove,
  onManage,
}: {
  group: GroupRow
  customer: string
  onSave: (details: GroupFields) => Promise<void>
  onRemove: () => void
  onManage: () => void
}) {
  const [name, setName] = useState(group.name)
  const [code, setCode] = useState(group.code ?? '')
  const [description, setDescription] = useState(group.description ?? '')
  const [saving, setSaving] = useState(false)
  return (
    <tr>
      <td>
        <input value={name} onChange={(e) => setName(e.target.value)} aria-label={`Name for ${group.name}`} />
      </td>
      <td>
        <input value={code} onChange={e => setCode(e.target.value)} maxLength={6} aria-label={`Group ID for ${group.name}`} />
      </td>
      <td>
        <input value={description} onChange={e => setDescription(e.target.value)} aria-label={`Description for ${group.name}`} />
      </td>
      <td>{customer}</td>
      <td>
        {group.terminalCount}{' '}
        <button type="button" className="quiet" onClick={onManage} aria-label={`Manage phones in ${group.name}`}>Manage</button>
      </td>
      <td className="row-actions">
        <button type="button" className="quiet" disabled={saving} onClick={() => {
          setSaving(true)
          void onSave({ name, code, description }).finally(() => setSaving(false))
        }}>
          Save group
        </button>
        <button type="button" className="quiet" onClick={onRemove}>
          Remove
        </button>
      </td>
    </tr>
  )
}
