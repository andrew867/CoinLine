import { useEffect, useState } from 'react'
import { apiGet, apiPostRaw } from '../api/client'

type SetChoice = { id: string; name: string; status: number; targetFirmware: string | null; customerId: string | null }
type SourceRecord = { id: string; tableNumber: number; definitionName: string; validationPassed: boolean; payloadLengthBytes: number }
type SourceDetail = { revision: number; versions: SourceRecord[] }
type CopyRequest = { sourceSetId: string; sourceRevision: number; destinationRevision: number; recordNumbers: number[] }
type CopyRecord = { tableNumber: number; definitionName: string; action: 'add' | 'replace' | 'keep'; required: boolean; warning?: string | null }

const actionLabels = { add: 'Add', replace: 'Replace', keep: 'Keep' }

export function TableRecordCopy({ setId, revision, customerId, disabled, onSaved }: {
  setId: string
  revision: number
  customerId: string | null
  disabled: boolean
  onSaved: () => void
}) {
  const [open, setOpen] = useState(false)
  const [choices, setChoices] = useState<SetChoice[] | null>(null)
  const [sourceId, setSourceId] = useState('')
  const [source, setSource] = useState<SourceDetail | null>(null)
  const [selected, setSelected] = useState<number[]>([])
  const [query, setQuery] = useState('')
  const [preview, setPreview] = useState<{ request: CopyRequest; records: CopyRecord[] } | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [status, setStatus] = useState('')
  const [reload, setReload] = useState(0)
  const eligible = choices?.filter((set) => set.id !== setId && (set.customerId === null || set.customerId === customerId))

  useEffect(() => {
    if (!open) return
    let current = true
    void apiGet<SetChoice[]>('/api/tables/sets')
      .then((data) => { if (current) setChoices(data) })
      .catch(() => { if (current) setError('Could not load saved table sets.') })
    return () => { current = false }
  }, [open, reload])

  useEffect(() => {
    if (!sourceId) return
    let current = true
    void apiGet<SourceDetail>(`/api/tables/sets/${sourceId}`)
      .then((data) => { if (current) setSource(data) })
      .catch(() => { if (current) setError('Could not load the source records.') })
    return () => { current = false }
  }, [sourceId, reload])

  function reset(sourceSetId: string) {
    setSourceId(sourceSetId)
    setSource(null)
    setSelected([])
    setPreview(null)
    setQuery('')
    setError('')
    setStatus('')
  }

  async function submit(request: CopyRequest, confirm: boolean) {
    setBusy(true)
    setError('')
    setStatus('')
    if (!confirm) setPreview(null)
    try {
      const response = await apiPostRaw(`/api/tables/sets/${setId}/record-copy${confirm ? '' : '/preview'}`, request)
      const data = await response.json().catch(() => null)
      if (!response.ok) throw new Error(data?.error ?? 'Could not copy records.')
      if (confirm) {
        const count = (data.records as CopyRecord[]).filter(({ action }) => action !== 'keep').length
        setPreview(null)
        setSelected([])
        setStatus(`Copied ${count} ${count === 1 ? 'record' : 'records'} into this draft.`)
        onSaved()
      } else {
        setPreview({ request, records: data.records })
      }
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : 'Could not copy records.')
    } finally {
      setBusy(false)
    }
  }

  const records = source?.versions.filter(({ definitionName, tableNumber }) =>
    `mt${tableNumber} ${definitionName}`.toLowerCase().includes(query.trim().toLowerCase())) ?? []
  const changes = preview?.records.filter(({ action }) => action !== 'keep').length ?? 0
  const stale = preview != null && preview.request.destinationRevision !== revision

  return (
    <section className="table-config-panel table-record-copy" aria-labelledby="record-copy-heading">
      <details open={open} onToggle={(event) => setOpen(event.currentTarget.open)}>
        <summary id="record-copy-heading">Copy saved records</summary>
        <form onSubmit={(event) => {
          event.preventDefault()
          if (source && selected.length && !busy && !disabled)
            void submit({ sourceSetId: sourceId, sourceRevision: source.revision, destinationRevision: revision, recordNumbers: selected }, false)
        }}>
          <div className="table-config-selectors">
            <label>Source table set
              <select value={sourceId} onChange={(event) => reset(event.target.value)} disabled={busy || !eligible?.length}>
                <option value="">{eligible ? 'Choose a set' : 'Loading sets…'}</option>
                {eligible?.map((set) => <option key={set.id} value={set.id}>
                  {set.name} ({set.status === 1 ? 'Published' : 'Draft'}{set.targetFirmware && `, ${set.targetFirmware}`})
                </option>)}
              </select>
            </label>
            <button type="button" disabled={busy || disabled} onClick={() => {
              reset(sourceId)
              setReload((value) => value + 1)
              onSaved()
            }}>Reload sets</button>
          </div>
          {eligible?.length === 0 && <p>No other table sets are available for this draft.</p>}
          {disabled && <p>Save the active edits before copying records.</p>}
          {sourceId && !source && !error && <p role="status">Loading records…</p>}
          {source && <fieldset disabled={busy || disabled}>
            <legend>Records to copy</legend>
            {source.versions.length > 12 && <label className="table-record-search">Find a record
              <input type="search" value={query} onChange={(event) => setQuery(event.target.value)} />
            </label>}
            {records.length === 0 && <p>{query ? 'No matching records.' : 'This set has no records.'}</p>}
            <div className="table-record-options">
              {records.map((record) => {
                const available = record.validationPassed && record.payloadLengthBytes > 2
                  && source.versions.filter(({ tableNumber }) => tableNumber === record.tableNumber).length === 1
                return <label key={record.id} className="table-record-option">
                  <input type="checkbox" disabled={!available} checked={selected.includes(record.tableNumber)}
                    onChange={(event) => {
                      setSelected(event.target.checked ? [...selected, record.tableNumber] : selected.filter((value) => value !== record.tableNumber))
                      setPreview(null)
                      setStatus('')
                    }} />
                  <span>MT{record.tableNumber} · {record.definitionName}
                    <small>{available ? `${record.payloadLengthBytes.toLocaleString()} bytes` : 'Unavailable for copying'}</small>
                  </span>
                </label>
              })}
            </div>
          </fieldset>}
          {source && <button type="submit" disabled={busy || disabled || selected.length === 0}>Review copy</button>}
        </form>
        {preview && <section className="table-config-preview" aria-labelledby="record-copy-review">
          <h3 id="record-copy-review">Copy review</h3>
          <ul>{preview.records.map((record) => <li key={record.tableNumber}>
            <strong>{actionLabels[record.action]} MT{record.tableNumber}</strong> {record.definitionName}{record.required && ' (required record)'}
            {record.warning && <p role="alert">{record.warning}</p>}
          </li>)}</ul>
          {stale && <p>The draft changed. Review the copy again.</p>}
          {!changes && <p>The draft already contains these records.</p>}
          <button type="button" disabled={busy || disabled || stale || changes === 0} onClick={() => void submit(preview.request, true)}>
            Copy {changes} {changes === 1 ? 'record' : 'records'} into draft
          </button>
        </section>}
        {error && <p role="alert">{error}</p>}
        {status && <p role="status">{status}</p>}
      </details>
    </section>
  )
}
