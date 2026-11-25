import { useEffect, useState } from 'react'
import { apiFetch } from '../api/client'

type Code = { code: number; description: string }
type Reference = { alarms: Code[]; maintenance: Code[] }

export function FieldEventReference() {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [reference, setReference] = useState<Reference | null>(null)
  const [error, setError] = useState(false)
  const [attempt, setAttempt] = useState(0)

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
    !needle || String(row.code).includes(needle) || row.description.toLowerCase().includes(needle))
  const groups = reference ? [
    { title: 'Alarms', rows: matching(reference.alarms) },
    { title: 'Maintenance opcodes', rows: matching(reference.maintenance) },
  ] : []

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
          <p>Built-in decoded descriptions. A code not listed here is still reported by number. Severity is not assigned by this reference.</p>
          <label>Find a code or description
            <input type="search" value={query} onChange={event => setQuery(event.target.value)} />
          </label>
          {error && <div role="alert">Could not load the reference. <button type="button" onClick={() => {
            setError(false)
            setAttempt(value => value + 1)
          }}>Retry</button></div>}
          {!reference && !error && <p role="status">Loading reference…</p>}
          {groups.map(group => (
            <section key={group.title} aria-label={group.title}>
              <h3>{group.title}</h3>
              {group.rows.length === 0 ? <p>No matching codes.</p> : (
                <table>
                  <thead><tr><th scope="col">Code</th><th scope="col">Description</th></tr></thead>
                  <tbody>{group.rows.map(row => <tr key={row.code}><td>{row.code}</td><td>{row.description}</td></tr>)}</tbody>
                </table>
              )}
            </section>
          ))}
        </div>
      )}
    </section>
  )
}
