import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch, apiGet } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import './TableSetConfiguration.css'

export type CarrierEntry = {
  id: string; code: string; description: string; tableNumber: number; revision: number; reference: string;
  displayName: string | null; featureGroupB: boolean; screeningEntry: string | null
}
type Field = { key: string; label: string; editor: string; value: string | null; min: number; max: number; maxLength: number | null; choices: { value: number; label: string }[] | null; help: string | null }
type Details = { entry: CarrierEntry; fields: Field[] }
type Schema = { fields: Field[] }
const endpoint = '/api/carrier-library'

async function checked(response: Response) {
  if (!response.ok) {
    const body = await response.json().catch(() => null)
    throw new Error(body?.error ?? `Request failed (${response.status}).`)
  }
  return response
}

export function CarrierBrandingPreview({ name }: { name: string }) {
  return <section className="table-config-panel" aria-label="Carrier branding preview">
    <h3>Carrier display preview</h3>
    <div className="table-config-vfd" role="img" aria-label={`Typical thank-you prompt, lower line ${name || 'blank'}`}>
      <div>{'Thank you for using'.padEnd(20)}</div><div>{name.slice(0, 20).padEnd(20)}</div>
    </div>
    <small>The carrier name is the lower line. The actual introductory text is supplied by the selected language's visual prompt record.</small>
  </section>
}

export function CarrierLibraryPage() {
  const canRead = canPerform('carrier-library', op.read), canAdd = canPerform('carrier-library', op.add)
  const canUpdate = canPerform('carrier-library', op.update), canDelete = canPerform('carrier-library', op.delete)
  const [entries, setEntries] = useState<CarrierEntry[]>([])
  const [number, setNumber] = useState(23)
  const [id, setId] = useState<string | null>(null)
  const [revision, setRevision] = useState<number | undefined>()
  const [fields, setFields] = useState<Field[] | null>(null)
  const [code, setCode] = useState(''), [description, setDescription] = useState('')
  const [changes, setChanges] = useState<Record<string, string>>({})
  const [storedCode, setStoredCode] = useState(''), [storedDescription, setStoredDescription] = useState('')
  const [reload, setReload] = useState(0)
  const [message, setMessage] = useState(''), [error, setError] = useState(''), [busy, setBusy] = useState(false)
  const editable = id ? canUpdate : canAdd

  function refresh() {
    if (!canRead) return
    void apiGet<CarrierEntry[]>(endpoint).then(setEntries).catch(() => setError('Could not load carrier definitions.'))
  }
  useEffect(refresh, [canRead]) // The permission decision is stable for this session.

  useEffect(() => {
    if (id ? !canRead : !canAdd) return
    const abort = new AbortController()
    setFields(null)
    setChanges({})
    setError('')
    if (!id) { setCode(''); setDescription(''); setStoredCode(''); setStoredDescription(''); setRevision(undefined) }
    const url = id ? `${endpoint}/${id}` : `${endpoint}/schema?tableNumber=${number}`
    void apiFetch(url, { signal: abort.signal }).then(checked).then(async response => {
      const data = await response.json() as Details | Schema
      if (abort.signal.aborted) return
      setFields(data.fields)
      if ('entry' in data) { setCode(data.entry.code); setDescription(data.entry.description); setStoredCode(data.entry.code); setStoredDescription(data.entry.description); setRevision(data.entry.revision); setNumber(data.entry.tableNumber) }
    }).catch((err: unknown) => { if (!abort.signal.aborted) setError(err instanceof Error ? err.message : 'Could not load the carrier editor.') })
    return () => abort.abort()
  }, [id, number, canRead, canAdd, reload])

  const dirty = Object.keys(changes).length > 0 || code !== storedCode || description !== storedDescription

  function select(entry: CarrierEntry | null) {
    if (dirty && !window.confirm('Discard unsaved carrier changes?')) return
    setMessage('')
    setId(entry?.id ?? null)
    if (entry) setNumber(entry.tableNumber)
    setReload(count => count + 1)
  }

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!editable || !fields) return
    setBusy(true); setError(''); setMessage('')
    try {
      const response = await checked(await apiFetch(id ? `${endpoint}/${id}` : endpoint, {
        method: id ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ code, description, tableNumber: number, changes, revision }),
      }))
      const data = await response.json() as Details
      setFields(data.fields); setChanges({}); setRevision(data.entry.revision); setStoredCode(data.entry.code); setStoredDescription(data.entry.description)
      setMessage('Carrier definition saved.')
      if (canRead) { setId(data.entry.id); refresh() }
      else { setCode(''); setDescription(''); setStoredCode(''); setStoredDescription(''); setFields((await apiGet<Schema>(`${endpoint}/schema?tableNumber=${number}`)).fields) }
    } catch (err) { setError(err instanceof Error ? err.message : 'Could not save the carrier.') }
    finally { setBusy(false) }
  }

  async function remove() {
    if (!id || !canDelete || !window.confirm('Delete this library definition? Existing table snapshots are not changed.')) return
    setBusy(true); setError(''); setMessage('')
    try {
      await checked(await apiFetch(`${endpoint}/${id}?revision=${revision}&confirm=true`, { method: 'DELETE' }))
      setId(null); setFields(null); setChanges({}); setMessage('Carrier definition deleted. Existing table records are unchanged.'); refresh()
    } catch (err) { setError(err instanceof Error ? err.message : 'Could not delete the carrier.') }
    finally { setBusy(false) }
  }

  const value = (field: Field) => changes[field.key] ?? field.value ?? ''
  const name = fields?.find(field => field.key === 'carrier-0.name')
  return <div>
    <p><Link to="/table-sets">Table sets</Link></p><h1>Carrier library</h1>
    <p className="page-intro">Create reusable carrier definitions for each terminal layout. Apply them to individual rows of a draft carrier table. Card acceptance and screening positions are assigned in that target set, and published snapshots never change when a library entry is edited.</p>
    {error && <p role="alert">{error}</p>}{message && <p role="status">{message}</p>}
    {canRead && <section className="table-config-panel"><h2>Saved carrier definitions</h2>
      {canAdd && <button type="button" onClick={() => select(null)} disabled={busy}>New carrier</button>}
      {entries.length === 0 ? <p>No carrier definitions yet.</p> : <div className="table-set-versions"><table>
        <thead><tr><th>Carrier ID</th><th>Wire table</th><th>Reference</th><th>Display name</th><th>Description</th></tr></thead>
        <tbody>{entries.map(entry => <tr key={entry.id}><td><button type="button" aria-pressed={id === entry.id} onClick={() => select(entry)} disabled={busy}>{entry.code}</button></td>
          <td>MT{entry.tableNumber}</td><td>{entry.reference}</td><td>{entry.displayName}</td><td>{entry.description}</td></tr>)}</tbody>
      </table></div>}
    </section>}
    {!id && canAdd && <label>Carrier table layout<select value={number} disabled={busy} onChange={event => {
      if (dirty && !window.confirm('Discard unsaved carrier changes?')) return
      setNumber(Number(event.target.value))
    }}><option value={23}>MT23, older Multi-pay</option><option value={135}>MT135, later Multi-pay</option></select></label>}
    {fields && <form className="native-table-editor" onSubmit={event => void save(event)}>
      <h2>{id ? `Edit carrier ${code}` : 'New carrier definition'}</h2>
      <fieldset disabled={!editable || busy} className="native-table-fields">
        <label>Carrier ID<input value={code} maxLength={6} required onChange={event => setCode(event.target.value)} /></label>
        <label>Description<input value={description} maxLength={80} onChange={event => setDescription(event.target.value)} /></label>
      </fieldset>
      {name && <CarrierBrandingPreview name={value(name)} />}
      <fieldset disabled={!editable || busy} className="native-table-fields"><legend>Carrier settings, MT{number}</legend>
        {fields.map(field => <label key={field.key} className={field.editor === 'checkbox' ? 'native-table-toggle' : 'native-table-field'}>
          {field.editor === 'checkbox' ? <><input type="checkbox" checked={value(field) === 'true'} onChange={event => setChanges(old => ({ ...old, [field.key]: String(event.target.checked) }))} />{field.label}</> : <>
            <span>{field.label}</span>
            {field.choices ? <select value={value(field)} onChange={event => setChanges(old => ({ ...old, [field.key]: event.target.value }))}>
              {!field.choices.some(choice => String(choice.value) === value(field)) && <option value={value(field)}>Stored value {value(field)}</option>}
              {field.choices.map(choice => <option value={choice.value} key={choice.value}>{choice.label}</option>)}</select> :
              <input type={field.editor === 'number' ? 'number' : 'text'} value={value(field)} min={field.editor === 'number' ? field.min : undefined}
                max={field.editor === 'number' ? field.max : undefined} maxLength={field.maxLength ?? undefined} onChange={event => setChanges(old => ({ ...old, [field.key]: event.target.value }))} />}
          </>}{field.help && <small>{field.help}</small>}
        </label>)}
      </fieldset>
      <div className="row-actions">{editable && <button type="submit" disabled={busy || !code.trim()}>Save carrier</button>}
        {id && canDelete && <button type="button" disabled={busy} onClick={() => void remove()}>Delete definition</button>}
      </div>
    </form>}
  </div>
}
