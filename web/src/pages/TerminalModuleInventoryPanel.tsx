import { useEffect, useState, type FormEvent } from 'react'
import { apiFetch } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'

type Inventory = { smartCityId: string | null; protonId: string | null; revision: number | null; updatedAtUtc: string | null; administrativeOnly: boolean }

export function TerminalModuleInventoryPanel({ terminalId }: { terminalId: string }) {
  const endpoint = `/api/terminals/${terminalId}/module-inventory`
  const canUpdate = canPerform('inventory', op.update)
  const [open, setOpen] = useState(false)
  const [reload, setReload] = useState(0)
  const [saved, setSaved] = useState<Inventory | null>(null)
  const [smartCity, setSmartCity] = useState('')
  const [proton, setProton] = useState('')
  const [editing, setEditing] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  useEffect(() => {
    if (!open) return
    const controller = new AbortController()
    let active = true
    setSaved(null); setError(''); setNotice(''); setEditing(false)
    void apiFetch(endpoint, { signal: controller.signal }).then(async response => {
      if (!response.ok) throw new Error(`Could not load module inventory (HTTP ${response.status}).`)
      return await response.json() as Inventory
    }).then(value => { if (active) setSaved(value) })
      .catch(reason => { if (active) setError(reason instanceof Error ? reason.message : 'Could not load module inventory.') })
    return () => { active = false; controller.abort() }
  }, [endpoint, open, reload])

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!saved || !canUpdate || busy) return
    setBusy(true); setError(''); setNotice('')
    try {
      const response = await apiFetch(endpoint, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({
        smartCityId: smartCity.trim() || null, protonId: proton.trim() || null, revision: saved.revision }) })
      const data = await response.json().catch(() => null)
      if (!response.ok) throw new Error(data?.error ?? `Could not save module identifiers (HTTP ${response.status}).`)
      setSaved(data as Inventory); setEditing(false); setNotice('Module inventory saved.')
    } catch (reason) { setError(reason instanceof Error ? reason.message : 'Could not save module identifiers.') }
    finally { setBusy(false) }
  }

  return <details onToggle={event => setOpen(event.currentTarget.open)}>
    <summary>E-purse module identifiers</summary>
    {open && <>
      <p>Record the configured SmartCity and Proton module identifiers for inventory. Keep leading zeroes and the identifier as shown by the equipment. Do not enter PINs, keys or card data. These entries do not configure or authorize a payment application and are not downloaded.</p>
      {error && <p role="alert">{error}</p>}
      {notice && <p role="status">{notice}</p>}
      {!saved && !error && <p role="status">Loading module inventory…</p>}
      {saved && (editing ? <form onSubmit={event => void save(event)}>
        <div className="phone-inventory-fields">
          <label>SmartCity module ID<input value={smartCity} maxLength={64} disabled={busy} onChange={event => setSmartCity(event.target.value)} /></label>
          <label>Proton module ID<input value={proton} maxLength={64} disabled={busy} onChange={event => setProton(event.target.value)} /></label>
        </div>
        <p>Leave a field blank to explicitly clear that identifier.</p>
        <div className="phone-inventory-actions"><button type="submit" disabled={busy}>Save module identifiers</button>
          <button type="button" className="quiet" disabled={busy} onClick={() => { setEditing(false); setError('') }}>Cancel module edit</button></div>
      </form> : <>
        <dl className="phone-inventory-values"><div><dt>SmartCity module ID</dt><dd>{saved.smartCityId ?? 'Not registered'}</dd></div>
          <div><dt>Proton module ID</dt><dd>{saved.protonId ?? 'Not registered'}</dd></div></dl>
        {saved.updatedAtUtc && <p>Last administrative update: {new Date(saved.updatedAtUtc).toLocaleString()}</p>}
        {canUpdate && <button type="button" disabled={busy} onClick={() => {
          setSmartCity(saved.smartCityId ?? ''); setProton(saved.protonId ?? ''); setError(''); setNotice(''); setEditing(true)
        }}>Edit module identifiers</button>}
      </>)}
      <button type="button" className="quiet" disabled={busy} onClick={() => setReload(value => value + 1)}>Reload module inventory</button>
    </>}
  </details>
}
