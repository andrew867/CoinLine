import { useEffect, useState } from 'react'
import { apiFetch } from '../api/client'
import { alarmSeverities, alarmSeverityLabel } from './fieldEventLabels'

type Kind = 'alarm' | 'maintenance'
type Code = { code: number; description: string | null; operatorDescription: string | null; severity: number | null; revision: number }
type MaintenanceExample = { code: number; description: string; configured: boolean }
type Reference = { alarms: Code[]; maintenance: Code[]; maintenanceExamples?: MaintenanceExample[]; canEdit: boolean }
type Edit = { kind: Kind; row: Code; description: string; severity: string }

export function FieldEventReference() {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [reference, setReference] = useState<Reference | null>(null)
  const [error, setError] = useState(false)
  const [attempt, setAttempt] = useState(0)
  const [edit, setEdit] = useState<Edit | null>(null)
  const [newKind, setNewKind] = useState<Kind>('alarm')
  const [newCode, setNewCode] = useState('')
  const [saving, setSaving] = useState(false)
  const [notice, setNotice] = useState('')
  const [saveError, setSaveError] = useState('')

  useEffect(() => {
    if (!open) return
    const controller = new AbortController()
    async function load() {
      try {
        const response = await apiFetch('/api/field-events/reference', { signal: controller.signal })
        if (!response.ok) throw new Error('Reference unavailable')
        const data = await response.json() as Reference
        if (!controller.signal.aborted) {
          setReference(data)
          setError(false)
        }
      } catch {
        if (!controller.signal.aborted) setError(true)
      }
    }
    void load()
    return () => controller.abort()
  }, [open, attempt])

  const needle = query.trim().toLowerCase()
  const matching = (rows: Code[]) => rows.filter(row =>
    !needle || String(row.code).includes(needle) || (row.description ?? '').toLowerCase().includes(needle)
    || (row.operatorDescription ?? '').toLowerCase().includes(needle))
  const groups = reference ? [
    { kind: 'alarm' as const, title: 'Alarms', rows: matching(reference.alarms) },
    { kind: 'maintenance' as const, title: 'Maintenance opcodes', rows: matching(reference.maintenance) },
  ] : []

  function startEdit(kind: Kind, row: Code) {
    setEdit({ kind, row, description: row.operatorDescription ?? '', severity: row.severity == null ? '' : String(row.severity) })
    setNotice('')
    setSaveError('')
  }

  function selectCode() {
    if (!reference?.canEdit || !/^\d+$/.test(newCode)) return
    const code = Number(newCode)
    if (!Number.isSafeInteger(code) || code > (newKind === 'alarm' ? 255 : 65535)) {
      setSaveError('Enter a code within the selected category range.')
      return
    }
    const rows = newKind === 'alarm' ? reference.alarms : reference.maintenance
    startEdit(newKind, rows.find(row => row.code === code) ??
      { code, description: null, operatorDescription: null, severity: null, revision: 0 })
  }

  async function save() {
    if (!edit || !reference?.canEdit || saving) return
    setSaving(true)
    setSaveError('')
    setNotice('')
    try {
      const response = await apiFetch(`/api/field-events/reference/${edit.kind}/${edit.row.code}`, {
        method: 'PUT', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ operatorDescription: edit.description, severity: edit.severity ? Number(edit.severity) : null, revision: edit.row.revision }),
      })
      if (!response.ok) {
        const body = await response.json().catch(() => null)
        throw new Error(body?.error ?? `Could not save (HTTP ${response.status}).`)
      }
      const row = await response.json() as Code
      const key = edit.kind === 'alarm' ? 'alarms' : 'maintenance'
      setReference(previous => previous && ({
        ...previous, [key]: [...previous[key].filter(entry => entry.code !== row.code), row].sort((left, right) => left.code - right.code),
      }))
      setEdit(null)
      setNotice(`Saved ${edit.kind} code ${row.code}.`)
    } catch (failure) {
      setSaveError(failure instanceof Error ? failure.message : 'Could not save.')
    } finally {
      setSaving(false)
    }
  }

  async function importMaintenanceExamples() {
    if (!reference?.canEdit || saving || !reference.maintenanceExamples?.some(row => !row.configured)) return
    setSaving(true); setSaveError(''); setNotice('')
    try {
      const response = await apiFetch('/api/field-events/reference/maintenance/examples', { method: 'POST' })
      if (!response.ok) {
        const body = await response.json().catch(() => null)
        throw new Error(body?.error ?? `Could not import examples (HTTP ${response.status}).`)
      }
      const result = await response.json() as { added: number; retained: number }
      setNotice(`Imported ${result.added} example descriptions; ${result.retained} existing settings retained.`)
      setEdit(null); setAttempt(value => value + 1)
    } catch (failure) { setSaveError(failure instanceof Error ? failure.message : 'Could not import examples.') }
    finally { setSaving(false) }
  }

  return (
    <section className="terminal-status-panel" aria-labelledby="field-event-reference-title">
      <header className="terminal-status-panel-heading">
        <h2 id="field-event-reference-title">Alarm and maintenance reference</h2>
        <button type="button" aria-expanded={open} onClick={() => setOpen(value => !value)}>
          {open ? 'Hide reference' : 'Show reference'}
        </button>
      </header>
      {open && (
        <div className="field-event-reference">
          <p>Built-in decoded descriptions remain distinct from operator classifications. A code not listed here is still reported by number. Changes affect current report presentation, not the original records or terminal downloads.</p>
          <label>Find a code or description
            <input type="search" value={query} onChange={event => setQuery(event.target.value)} />
          </label>
          {error && <div role="alert">Could not load the reference. <button type="button" onClick={() => {
            setError(false)
            setAttempt(value => value + 1)
          }}>Retry</button></div>}
          {!reference && !error && <p role="status">Loading reference…</p>}
          {reference?.canEdit && (
            <div className="field-event-code-picker">
              <label>Code category<select value={newKind} onChange={event => setNewKind(event.target.value as Kind)}><option value="alarm">Alarm</option><option value="maintenance">Maintenance</option></select></label>
              <label>Code number<input value={newCode} inputMode="numeric" onChange={event => setNewCode(event.target.value)} /></label>
              <button type="button" disabled={saving || !/^\d+$/.test(newCode)} onClick={selectCode}>Configure code</button>
            </div>
          )}
          {saveError && <p role="alert">{saveError}</p>}
          {notice && <p role="status">{notice}</p>}
          {edit && reference?.canEdit && (
            <section className="field-event-code-editor" aria-label="Configure event code">
              <h3>{edit.kind === 'alarm' ? 'Alarm' : 'Maintenance opcode'} {edit.row.code}</h3>
              <p>Decoded description: {edit.row.description ?? 'Unlisted code'}</p>
              <label>Operator description<input maxLength={160} disabled={saving} value={edit.description} onChange={event => setEdit({ ...edit, description: event.target.value })} /></label>
              {edit.kind === 'alarm' && <label>Severity<select disabled={saving} value={edit.severity} onChange={event => setEdit({ ...edit, severity: event.target.value })}>
                <option value="">Unclassified</option>
                {alarmSeverities.map(severity => <option key={severity.value} value={severity.value}>{severity.value}: {severity.label}</option>)}
              </select></label>}
              <p>Leave the description blank and severity unclassified to clear operator settings.</p>
              <div className="field-event-code-actions">
                <button type="button" disabled={saving} onClick={() => void save()}>Save classification</button>
                <button type="button" disabled={saving} onClick={() => { setEdit(null); setSaveError('') }}>Cancel</button>
                <button type="button" disabled={saving} onClick={() => { setEdit(null); setSaveError(''); setAttempt(value => value + 1) }}>Reload catalogue</button>
              </div>
            </section>
          )}
          {!!reference?.maintenanceExamples?.length && <section aria-label="Historical maintenance examples">
            <h3>Historical maintenance examples</h3>
            <p>These are example field-service descriptions, not reserved system codes or remote commands. Review or customize them for your operation. Import adds only missing descriptions and retains any existing or intentionally cleared settings.</p>
            <details><summary>Review {reference.maintenanceExamples.length} example codes</summary>
              <table><thead><tr><th>Code</th><th>Example description</th><th>Catalogue</th></tr></thead>
                <tbody>{reference.maintenanceExamples.map(row => <tr key={row.code}><td>{row.code}</td><td>{row.description}</td><td>{row.configured ? 'Existing setting retained' : 'Not imported'}</td></tr>)}</tbody>
              </table>
            </details>
            {reference.canEdit && <button type="button" disabled={saving || !reference.maintenanceExamples.some(row => !row.configured)} onClick={() => void importMaintenanceExamples()}>
              Import {reference.maintenanceExamples.filter(row => !row.configured).length} missing example descriptions
            </button>}
          </section>}
          {groups.map(group => (
            <section key={group.title} aria-label={group.title}>
              <h3>{group.title}</h3>
              {group.rows.length === 0 ? <p>No matching codes.</p> : (
                <table>
                  <thead><tr><th scope="col">Code</th><th scope="col">Decoded description</th><th scope="col">Operator description</th>{group.kind === 'alarm' && <th scope="col">Severity</th>}{reference?.canEdit && <th scope="col">Edit</th>}</tr></thead>
                  <tbody>{group.rows.map(row => <tr key={row.code}><td>{row.code}</td><td>{row.description ?? 'Unlisted code'}</td><td>{row.operatorDescription ?? 'Not set'}</td>{group.kind === 'alarm' && <td>{alarmSeverityLabel(row.severity)}</td>}{reference?.canEdit && <td><button type="button" disabled={saving} onClick={() => startEdit(group.kind, row)} aria-label={`Edit ${group.kind} ${row.code}`}>Edit</button></td>}</tr>)}</tbody>
                </table>
              )}
            </section>
          ))}
        </div>
      )}
    </section>
  )
}
