import { useEffect, useState, type FormEvent } from 'react'
import { apiGet } from '../api/client'
import './TerminalInventoryPanel.css'

type Value = string | number | boolean | null
type Profile = Record<string, Value>
type SavedProfile = { profile: Profile; revision: number | null }
type Inventory = SavedProfile & {
  location: string | null
  customer: string | null
  serialNumber: string | null
  serialReportedAtUtc: string | null
}

type Field = {
  key: string
  label: string
  kind?: 'digits' | 'count' | 'cost' | 'check'
  length?: number
}

const sections: { title: string; fields: Field[] }[] = [
  {
    title: 'Site details',
    fields: [
      { key: 'regionCode', label: 'Region code', length: 32 },
      { key: 'route', label: 'Route', length: 24 },
      { key: 'stop', label: 'Stop', length: 24 },
      { key: 'siteDescription', label: 'Site description', length: 120 },
      { key: 'addressLine1', label: 'Address line 1', length: 120 },
      { key: 'addressLine2', label: 'Address line 2', length: 120 },
      { key: 'addressLine3', label: 'Address line 3', length: 120 },
      { key: 'contact', label: 'Site contact', length: 80 },
      { key: 'indoor', label: 'Indoors', kind: 'check' },
      { key: 'basement', label: 'Basement', kind: 'check' },
    ],
  },
  {
    title: 'Administration',
    fields: [
      { key: 'serviceClass', label: 'Service class', length: 24 },
      { key: 'publicService', label: 'Public service terminal', kind: 'check' },
      { key: 'salespersonCode', label: 'Salesperson code', kind: 'digits', length: 7 },
      { key: 'revenueAccountingCode', label: 'Revenue accounting code', kind: 'digits', length: 3 },
      { key: 'billingCustomerCode', label: 'Billing customer code', kind: 'digits', length: 3 },
      { key: 'voucherAccountCode', label: 'Voucher account code', kind: 'digits', length: 7 },
      { key: 'industryCode', label: 'Industry code (SIC)', kind: 'digits', length: 5 },
      { key: 'locationGroupCode', label: 'Location group code', kind: 'digits', length: 6 },
    ],
  },
  {
    title: 'Equipment and costs',
    fields: [
      { key: 'equipmentCost', label: 'Equipment cost', kind: 'cost' },
      { key: 'loopMileageCost', label: 'Loop and mileage cost', kind: 'cost' },
      { key: 'housingCode', label: 'Housing code', length: 24 },
      { key: 'directoryCode', label: 'Directory facilities code', length: 24 },
      { key: 'directoryCount', label: 'Directory facilities quantity', kind: 'count' },
      { key: 'signCode', label: 'Signs code', length: 24 },
      { key: 'signCount', label: 'Signs quantity', kind: 'count' },
      { key: 'extensionCode', label: 'Extensions code', length: 24 },
      { key: 'extensionCount', label: 'Extensions quantity', kind: 'count' },
      { key: 'miscellaneousCode', label: 'Other equipment code', length: 24 },
      { key: 'miscellaneousCount', label: 'Other equipment quantity', kind: 'count' },
    ],
  },
]

function saveValue(field: Field, value: Value | undefined): Value {
  if (field.kind === 'check') return value === true
  const text = String(value ?? '').trim()
  if (!text) return null
  return field.kind === 'cost' || field.kind === 'count' ? Number(text) : text
}

function InventoryInput({ field, value, onChange, disabled }: {
  field: Field; value: Value | undefined; onChange: (value: Value) => void; disabled: boolean
}) {
  if (field.kind === 'check') return <label className="phone-inventory-check">
    <input type="checkbox" checked={value === true} disabled={disabled} onChange={event => onChange(event.target.checked)} />
    {field.label}
  </label>
  const numeric = field.kind === 'cost' || field.kind === 'count'
  return <label>
    {field.label}
    <input
      type={numeric ? 'number' : 'text'}
      value={String(value ?? '')}
      maxLength={field.length}
      min={numeric ? 0 : undefined}
      max={field.kind === 'cost' ? 9999999999.99 : field.kind === 'count' ? 9999 : undefined}
      step={field.kind === 'cost' ? 0.01 : undefined}
      inputMode={field.kind === 'digits' ? 'numeric' : field.kind === 'cost' ? 'decimal' : undefined}
      pattern={field.kind === 'digits' ? '[0-9]+' : undefined}
      disabled={disabled}
      onChange={event => onChange(event.target.value)}
    />
  </label>
}

export function TerminalInventoryPanel({ terminalId }: { terminalId: string }) {
  const endpoint = `/api/terminals/${terminalId}/inventory`
  const [saved, setSaved] = useState<Inventory | null>(null)
  const [draft, setDraft] = useState<Profile>({})
  const [editing, setEditing] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  useEffect(() => {
    let current = true
    void apiGet<Inventory>(endpoint).then(data => { if (current) setSaved(data) })
      .catch(() => { if (current) setError('Could not load terminal inventory.') })
    return () => { current = false }
  }, [endpoint])

  async function reload() {
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const current = await apiGet<Inventory>(endpoint)
      setSaved(current)
      setDraft(current.profile)
    } catch {
      setError('Could not load terminal inventory.')
    } finally {
      setBusy(false)
    }
  }

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!saved) return
    setBusy(true)
    setError('')
    setNotice('')
    try {
      const profile = Object.fromEntries(sections.flatMap(section =>
        section.fields.map(field => [field.key, saveValue(field, draft[field.key])])))
      const response = await fetch(endpoint, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json', 'X-Operator-Id': 'ui@local', 'X-Operator-Role': 'Admin' },
        body: JSON.stringify({ revision: saved.revision, profile }),
      })
      const data = await response.json()
      if (!response.ok) {
        const validation = Object.values(data.errors ?? {}).flat().join(' ')
        throw new Error(data.error ?? (validation || 'Could not save terminal inventory.'))
      }
      setSaved({ ...saved, ...(data as SavedProfile) })
      setEditing(false)
      setNotice('Terminal inventory saved.')
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Could not save terminal inventory.')
    } finally {
      setBusy(false)
    }
  }

  return <section className="table-config-panel phone-inventory" aria-labelledby="phone-inventory-heading">
    <h2 id="phone-inventory-heading">Terminal inventory</h2>
    {saved && <dl className="phone-inventory-facts">
      <div><dt>Registered location</dt><dd>{saved.location ?? 'Unknown'}</dd></div>
      <div><dt>Customer</dt><dd>{saved.customer ?? 'Unknown'}</dd></div>
      <div>
        <dt>Reported hardware serial</dt>
        <dd>{saved.serialNumber ?? 'Awaiting a status report'}
          {saved.serialReportedAtUtc && <small>{new Date(saved.serialReportedAtUtc).toLocaleString()}</small>}
        </dd>
      </div>
    </dl>}
    {editing ? <form onSubmit={event => void save(event)}>
      {sections.map(section => <fieldset key={section.title}>
        <legend>{section.title}</legend>
        <div className="phone-inventory-fields">
          {section.fields.map(field => <InventoryInput key={field.key} field={field} value={draft[field.key]}
            disabled={busy} onChange={value => setDraft(previous => ({ ...previous, [field.key]: value }))} />)}
        </div>
      </fieldset>)}
      <div className="phone-inventory-actions">
        <button disabled={busy}>Save inventory</button>
        <button type="button" className="quiet" disabled={busy} onClick={() => { setEditing(false); setError('') }}>Cancel</button>
        <button type="button" className="quiet" disabled={busy} onClick={() => void reload()}>Reload inventory</button>
      </div>
    </form> : <>
      {saved && sections.map(section => {
        const populated = section.fields.filter(field => {
          const value = saved.profile[field.key]
          return value != null && value !== '' && value !== false
        })
        return populated.length > 0 && <details key={section.title} open={section.title === 'Site details'}>
          <summary>{section.title}</summary>
          <dl className="phone-inventory-values">
            {populated.map(field => <div key={field.key}>
              <dt>{field.label}</dt>
              <dd>{field.kind === 'check' ? 'Yes' : field.kind === 'cost'
                ? Number(saved.profile[field.key]).toFixed(2) : saved.profile[field.key]}</dd>
            </div>)}
          </dl>
        </details>
      })}
      <div className="phone-inventory-actions">
        {saved && <button type="button" disabled={busy} onClick={() => {
          setDraft(saved.profile)
          setEditing(true)
          setNotice('')
          setError('')
        }}>Edit inventory</button>}
        <button type="button" className="quiet" disabled={busy} onClick={() => void reload()}>Refresh inventory</button>
      </div>
    </>}
    {error && <p role="alert">{error}</p>}
    {notice && <p role="status">{notice}</p>}
  </section>
}
