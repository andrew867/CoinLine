import { useEffect, useRef, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, apiGet } from '../api/client'
import { ErrorBanner } from '../components/AppStates'
import { canPerform, permissionOperation as op } from '../api/consoleSession'

type Group = { id: string; name: string }
type Phone = {
  id: string; displayName: string; terminalIdHex: string; revision: number
  member: boolean; primary: boolean; eligible: boolean; atLimit: boolean
}

export function GroupPhones({ group, groups, onSaved }: { group: Group; groups: Group[]; onSaved: () => Promise<void> }) {
  const canAdd = canPerform('memberships', op.add)
  const canDelete = canPerform('memberships', op.delete)
  const canReadPhones = canPerform('phones', op.read)
  const [phones, setPhones] = useState<Phone[] | null>(null)
  const [edits, setEdits] = useState<Record<string, boolean>>({})
  const [source, setSource] = useState('')
  const [query, setQuery] = useState('')
  const [membersOnly, setMembersOnly] = useState(true)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const heading = useRef<HTMLHeadingElement>(null)
  const endpoint = `/api/terminal-groups/${group.id}/phones`

  useEffect(() => {
    heading.current?.focus()
    let active = true
    void apiGet<Phone[]>(endpoint).then(rows => { if (active) setPhones(rows) })
      .catch(() => { if (active) setError('Could not load the phones for this group.') })
    return () => { active = false }
  }, [endpoint])

  async function copyPhones() {
    if (!phones || !source || !canAdd) return
    setBusy(true)
    setError('')
    setMessage('')
    try {
      const sourcePhones = await apiGet<Phone[]>(`/api/terminal-groups/${source}/phones?membersOnly=true`)
      const wanted = new Set(sourcePhones.map(phone => phone.id))
      const available = phones.filter(phone => wanted.has(phone.id) && (phone.member || (phone.eligible && !phone.atLimit)))
      const changes = { ...edits }
      for (const phone of available) {
        if (phone.member) delete changes[phone.id]
        else changes[phone.id] = true
      }
      const blocked = sourcePhones.length - available.length
      const added = available.filter(phone => !phone.member && !edits[phone.id]).length
      setEdits(changes)
      setMembersOnly(true)
      setQuery('')
      setMessage(`${added} phones added to the selection. ${blocked} unavailable in this group.`)
    } catch {
      setError('Could not copy phones from the selected group.')
    } finally {
      setBusy(false)
    }
  }

  async function save() {
    if (!phones || (!canAdd && !canDelete)) return
    setBusy(true)
    setError('')
    setMessage('')
    try {
      const response = await apiFetch(endpoint, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(phones.filter(phone => phone.id in edits).map(phone => ({
          terminalId: phone.id, revision: phone.revision, member: edits[phone.id],
        }))),
      })
      const result = await response.json().catch(() => null)
      if (!response.ok) throw new Error(result?.error ?? 'Could not save group memberships.')
      setEdits({})
      setMessage(`${result.changed} phone memberships updated.`)
      try {
        const [current] = await Promise.all([apiGet<Phone[]>(endpoint), onSaved()])
        setPhones(current)
      } catch {
        setError('The changes were saved. Reload the page to refresh the memberships.')
      }
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Could not save group memberships.')
    } finally {
      setBusy(false)
    }
  }

  const search = query.trim().toLowerCase()
  const visible = phones?.filter(phone => (!membersOnly || phone.member || edits[phone.id])
    && (phone.displayName.toLowerCase().includes(search) || phone.terminalIdHex.toLowerCase().includes(search)))
  const changes = Object.keys(edits).length
  return <section aria-labelledby="group-phones-heading" className="table-config-panel">
    <h2 ref={heading} tabIndex={-1} id="group-phones-heading">Phones in {group.name}</h2>
    {error && <ErrorBanner message={error} />}
    {message && <p role="status">{message}</p>}
    {phones && <>
      {canAdd && <div className="row-actions">
        <label>Copy phones from group
          <select value={source} disabled={busy} onChange={event => setSource(event.target.value)}>
            <option value="">Choose a source group</option>
            {groups.filter(other => other.id !== group.id).map(other => <option key={other.id} value={other.id}>{other.name}</option>)}
          </select>
        </label>
        <button type="button" disabled={busy || !source} onClick={() => void copyPhones()}>Review copied phones</button>
      </div>}
      <div className="row-actions">
        <label>Find phones
          <input type="search" value={query} onChange={event => setQuery(event.target.value)} />
        </label>
        <label><input type="checkbox" checked={membersOnly} onChange={event => setMembersOnly(event.target.checked)} /> Show members only</label>
      </div>
      {visible?.length === 0 && <p>No matching phones.</p>}
      {!!visible?.length && <table>
        <thead><tr><th>Phone</th><th>Terminal ID</th><th>Membership</th></tr></thead>
        <tbody>{visible.map(phone => <tr key={phone.id}>
          <td>{canReadPhones ? <Link to={`/terminals/${phone.id}`}>{phone.displayName}</Link> : phone.displayName}</td>
          <td>{phone.terminalIdHex}</td>
          <td><label><input type="checkbox" checked={edits[phone.id] ?? phone.member}
            disabled={busy || (phone.member ? !canDelete : !canAdd || !phone.eligible || phone.atLimit)}
            aria-label={`Membership for ${phone.displayName} ${phone.terminalIdHex}`}
            onChange={event => {
              const pending = { ...edits }
              if (event.target.checked === phone.member) delete pending[phone.id]
              else pending[phone.id] = event.target.checked
              setEdits(pending)
              setMessage('')
            }} /> {phone.id in edits ? (edits[phone.id] ? 'Add' : 'Remove')
              : phone.primary ? 'Primary' : phone.member ? 'Member' : phone.atLimit ? 'Group limit reached' : 'Not a member'}</label></td>
        </tr>)}</tbody>
      </table>}
      <button type="button" disabled={busy || changes === 0 || (!canAdd && !canDelete)} onClick={() => void save()}>
        Save {changes === 1 ? '1 membership change' : `${changes} membership changes`}
      </button>
    </>}
  </section>
}
