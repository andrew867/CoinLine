import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { apiFetch } from '../api/client'
import { canPerform, permissionOperation as op } from '../api/consoleSession'
import { CustomerReferenceSearch, quickAccessCustomerEndpoint as endpoint, quickAccessCustomerResponse, type QuickAccessCustomerSummary } from './QuickAccessCustomerLookup'

const fields = [
  { key: 'name', label: 'Customer name', max: 160, required: true },
  { key: 'billingComments', label: 'Billing comments', max: 2000, multiline: true },
  { key: 'contactName', label: 'Contact name', max: 40 },
  { key: 'phone', label: 'Contact phone', max: 64 },
  { key: 'street', label: 'Street address', max: 256 },
  { key: 'city', label: 'City', max: 128 },
  { key: 'province', label: 'Province or state', max: 128 },
  { key: 'postalCode', label: 'Postal or ZIP code', max: 32 },
  { key: 'notes', label: 'Notes', max: 2000, multiline: true },
] as const
type Detail = QuickAccessCustomerSummary & Record<(typeof fields)[number]['key'], string>
const blank = (): Detail => ({ id: '', reference: '', revision: 0, archived: false, name: '', billingComments: '',
  contactName: '', phone: '', street: '', city: '', province: '', postalCode: '', notes: '' })
const validReference = (value: string) => /^[0-9]{6}$/.test(value) && value !== '000000'

export function QuickAccessCustomersPage() {
  const canRead = canPerform('quick-access-customers', op.read)
  const canAdd = canPerform('quick-access-customers', op.add)
  const canUpdate = canPerform('quick-access-customers', op.update)
  const canArchive = canPerform('quick-access-customers', op.delete)
  const [id, setId] = useState('')
  const [details, setDetails] = useState<Detail | null>(canAdd ? blank() : null)
  const [baseline, setBaseline] = useState<Detail | null>(canAdd ? blank() : null)
  const [archived, setArchived] = useState(false)
  const [loading, setLoading] = useState(false)
  const [busy, setBusy] = useState(false)
  const [notice, setNotice] = useState('')
  const [error, setError] = useState('')
  const [refresh, setRefresh] = useState(0)
  const [recordRefresh, setRecordRefresh] = useState(0)
  const dirty = JSON.stringify(details) !== JSON.stringify(baseline)
  const editable = !!details && !details.archived && (details.id ? canUpdate : canAdd)
  const setValue = (key: keyof Detail, value: string) => setDetails(old => old ? { ...old, [key]: value } : null)

  useEffect(() => {
    if (!id || !canRead) return
    const controller = new AbortController()
    setLoading(true); setError('')
    void apiFetch(`${endpoint}/${id}`, { signal: controller.signal }).then(quickAccessCustomerResponse<Detail>).then(data => {
      if (!controller.signal.aborted) { setDetails(data); setBaseline(data) }
    }).catch(err => {
      if (!controller.signal.aborted) {
        setDetails(null); setBaseline(null)
        setError(err instanceof Error ? err.message : 'Could not open the customer.')
      }
    }).finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [id, canRead, recordRefresh])

  function select(row: QuickAccessCustomerSummary | null) {
    if (row?.id === id) return
    if (dirty && !window.confirm('Discard unsaved customer changes?')) return
    setNotice(''); setError(''); setDetails(row ? null : blank()); setBaseline(row ? null : blank())
    setId(row?.id ?? '')
  }
  async function save(event: React.FormEvent) {
    event.preventDefault()
    if (!details || !editable || busy) return
    setBusy(true); setNotice(''); setError('')
    try {
      const { id: entryId, archived: ignored, ...body } = details
      void ignored
      const saved = await quickAccessCustomerResponse<Detail>(await apiFetch(entryId ? `${endpoint}/${entryId}` : endpoint, {
        method: entryId ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body),
      }))
      setDetails(saved); setBaseline(saved); setId(saved.id); setRefresh(old => old + 1)
      setNotice('Customer directory entry saved.')
    } catch (err) { setError(err instanceof Error ? err.message : 'Could not save the customer.') }
    finally { setBusy(false) }
  }
  async function archive(restore: boolean) {
    if (!details?.id || dirty || busy) return
    if (!restore && !window.confirm('Archive this customer? Existing quick access keys keep their stored reference.')) return
    setBusy(true); setError(''); setNotice('')
    try {
      await quickAccessCustomerResponse(await apiFetch(
        `${endpoint}/${details.id}${restore ? '/restore' : ''}?revision=${details.revision}${restore ? '' : '&confirm=true'}`,
        { method: restore ? 'POST' : 'DELETE' }))
      if (canRead) setRecordRefresh(old => old + 1)
      else {
        const updated = { ...details, archived: !restore, revision: details.revision + 1 }
        setDetails(updated); setBaseline(updated)
      }
      setRefresh(old => old + 1)
      setNotice(restore ? 'Customer restored.' : 'Customer archived. Existing terminal records were not changed.')
    } catch (err) { setError(err instanceof Error ? err.message : 'Could not update the directory entry.') }
    finally { setBusy(false) }
  }
  return <div className="quick-access-customer-directory">
    <p><Link to="/table-sets">Table sets</Link></p>
    <h1>Quick Access Key Customer List</h1>
    <p className="page-intro">Manage the customer reference directory used by the MT55 quick access key editor. Customer contact details and billing notes stay on the host. A terminal receives only its numeric reference.</p>
    {!canRead && !canAdd && <p role="status">You do not have permission to view or add directory entries.</p>}
    {canRead && <section className="table-config-panel"><h2>Customer directory</h2>
      <div className="row-actions">{canAdd && <button type="button" disabled={busy || loading} onClick={() => select(null)}>New customer</button>}
        <label><input type="checkbox" checked={archived} disabled={busy} onChange={event => setArchived(event.target.checked)} /> Include archived customers</label></div>
      <CustomerReferenceSearch<QuickAccessCustomerSummary> mode="search" includeArchived={archived} refresh={refresh}
        selectedReference={details?.reference} disabled={busy || loading} onSelect={select} />
    </section>}
    {loading && <p role="status">Opening customer…</p>}
    {error && <p role="alert">{error} {id && canRead && <button type="button" className="quiet" disabled={busy || loading}
      onClick={() => { if (!dirty || window.confirm('Discard unsaved customer changes?')) setRecordRefresh(old => old + 1) }}>Reload customer</button>}</p>}
    {notice && <p role="status">{notice}</p>}
    {details && !loading && <form className="native-table-editor" onSubmit={event => void save(event)}>
      <h2>{details.id ? `Customer ${details.reference}` : 'New customer'}</h2>
      {details.archived && <p>This directory entry is archived and is no longer offered in the key lookup.</p>}
      <fieldset className="native-table-fields" disabled={!editable || busy}>
        <legend>Customer identification</legend>
        <label>Customer reference<input inputMode="numeric" pattern="[0-9]{6}" required maxLength={6} value={details.reference}
          readOnly={!!details.id} onChange={event => setValue('reference', event.target.value)} /></label>
        <p className="quick-access-customer-help">Enter six digits, other than 000000. References cannot be reassigned after creation.</p>
        {fields.slice(0, 2).map(field => <label key={field.key}>{field.label}
          {'multiline' in field ? <textarea maxLength={field.max} value={details[field.key]} onChange={event => setValue(field.key, event.target.value)} />
            : <input required={'required' in field} maxLength={field.max} value={details[field.key]} onChange={event => setValue(field.key, event.target.value)} />}
        </label>)}
      </fieldset>
      <fieldset className="native-table-fields" disabled={!editable || busy}>
        <legend>Contact and notes</legend>
        {fields.slice(2).map(field => <label key={field.key}>{field.label}
          {'multiline' in field ? <textarea maxLength={field.max} value={details[field.key]} onChange={event => setValue(field.key, event.target.value)} />
            : <input maxLength={field.max} value={details[field.key]} onChange={event => setValue(field.key, event.target.value)} />}
        </label>)}
      </fieldset>
      <div className="row-actions">
        {editable && <button type="submit" disabled={busy || !validReference(details.reference) || !details.name.trim() || !dirty}>Save customer</button>}
        {editable && dirty && <button type="button" className="quiet" disabled={busy} onClick={() => setDetails(baseline)}>Discard changes</button>}
        {details.id && !details.archived && canArchive && <button type="button" className="quiet" disabled={busy || dirty} onClick={() => void archive(false)}>Archive customer</button>}
        {details.archived && canUpdate && <button type="button" disabled={busy} onClick={() => void archive(true)}>Restore customer</button>}
        {!canRead && canAdd && details.id && <button type="button" disabled={busy} onClick={() => select(null)}>New customer</button>}
      </div>
    </form>}
  </div>
}
