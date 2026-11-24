import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { apiFetch, apiGet } from '../api/client'
import './InmateFeaturesPage.css'

type Setting = boolean | number | string | null
type Field = {
  key: string
  label: string
  kind: 'checkbox' | 'number' | 'select' | 'dtmf'
  defaultValue: Setting
  minimum: number | null
  maximum: number | null
  choices: { value: number; label: string }[] | null
}
type Schema = { nameMaxLength: number; descriptionMaxLength: number; sections: { key: string; label: string; fields: Field[] }[] }
type Row = { id: string; name: string; description: string }
type Draft = Omit<Row, 'id'> & { id?: string; revision?: number; settings: Record<string, Setting> }
type Errors = Record<string, string[]>
type Failure = { error?: string; errors?: Errors }

const route = '/inmate-features'
const endpoint = '/api/inmate-features'

export function InmateFeaturesPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const [rows, setRows] = useState<Row[] | null>(null)
  const [schema, setSchema] = useState<Schema | null>(null)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')

  const loadRows = useCallback(() => {
    void apiGet<Row[]>(endpoint).then(setRows).catch(() => setError('Could not load saved configurations.'))
  }, [])

  useEffect(() => {
    loadRows()
    void apiGet<Schema>(`${endpoint}/schema`).then(setSchema).catch(() => setError('Could not load the configuration editor.'))
  }, [loadRows])

  function saved(configurationId: string) {
    loadRows()
    if (id !== configurationId) navigate(`${route}/${configurationId}`)
    setNotice('Configuration saved.')
  }

  function deleted() {
    loadRows()
    navigate(route)
    setNotice('Configuration deleted.')
  }

  return (
    <div className="inmate-config">
      <p><Link to="/table-sets">Table sets</Link></p>
      <h1>Inmate Feature Configurations</h1>
      <p className="page-intro">Save named call, PIN and recording configurations. Terminal download requires a verified Inmate firmware layout.</p>
      {error && <p role="alert">{error}</p>}
      {notice && <p role="status">{notice}</p>}
      <div className="inmate-config-layout">
        <nav className="inmate-config-saved" aria-label="Saved configurations">
          <div className="inmate-config-heading">
            <h2>Saved configurations</h2>
            {id && <Link to={route} onClick={() => setNotice('')}>New configuration</Link>}
          </div>
          {!rows && !error && <p>Loading configurations…</p>}
          {rows?.length === 0 && <p>No configurations yet.</p>}
          {rows && rows.length > 0 && <ul>{rows.map((row) => <li key={row.id}>
            <Link to={`${route}/${row.id}`} onClick={() => setNotice('')} aria-current={row.id === id ? 'page' : undefined}>
              <strong>{row.name}</strong>
              {row.description && <span>{row.description}</span>}
            </Link>
          </li>)}</ul>}
        </nav>
        {schema && <InmateFeatureEditor key={id ?? 'new'} id={id} schema={schema} onSaved={saved} onDeleted={deleted}
          onEdit={() => setNotice('')} />}
      </div>
    </div>
  )
}

function InmateFeatureEditor({ id, schema, onSaved, onDeleted, onEdit }: {
  id: string | undefined
  schema: Schema
  onSaved: (configurationId: string) => void
  onDeleted: () => void
  onEdit: () => void
}) {
  const [draft, setDraft] = useState<Draft | null>(() => id ? null : {
    name: '', description: '', settings: Object.fromEntries(schema.sections.flatMap(section =>
      section.fields.map(field => [field.key, field.defaultValue])))
  })
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<Failure>({})

  useEffect(() => {
    if (!id) return
    let current = true
    void apiGet<Draft>(`${endpoint}/${id}`)
      .then(configuration => { if (current) setDraft(configuration) })
      .catch(() => { if (current) setFailure({ error: 'Could not open this configuration.' }) })
    return () => { current = false }
  }, [id])

  function edit(change: (value: Draft) => Draft) {
    onEdit()
    setFailure({})
    setDraft(current => current && change(current))
  }

  async function save(event: FormEvent) {
    event.preventDefault()
    if (!draft) return
    onEdit()
    setBusy(true)
    setFailure({})
    try {
      const response = await apiFetch(draft.id ? `${endpoint}/${draft.id}` : endpoint, {
        method: draft.id ? 'PUT' : 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(draft),
      })
      if (!response.ok) {
        setFailure(await readFailure(response))
        return
      }
      const saved = await response.json() as Draft & { id: string }
      setDraft(saved)
      onSaved(saved.id)
    } catch {
      setFailure({ error: 'Could not save the configuration.' })
    } finally {
      setBusy(false)
    }
  }

  async function remove() {
    if (!draft?.id || !window.confirm(`Delete the ${draft.name} configuration?`)) return
    onEdit()
    setBusy(true)
    setFailure({})
    try {
      const response = await apiFetch(`${endpoint}/${draft.id}?confirm=true&revision=${draft.revision}`, {
        method: 'DELETE',
      })
      if (response.ok) onDeleted()
      else setFailure(await readFailure(response))
    } catch {
      setFailure({ error: 'Could not delete the configuration.' })
    } finally {
      setBusy(false)
    }
  }

  if (!draft) return <p role={failure.error ? 'alert' : 'status'}>{failure.error ?? 'Loading configuration…'}</p>

  const errors = failure.errors ?? {}
  return (
    <form onSubmit={(event) => void save(event)} aria-label="Inmate feature configuration" className="inmate-config-editor">
      <h2>{draft.id ? `Edit ${draft.name}` : 'New configuration'}</h2>
      {failure.error && <p role="alert">{failure.error}</p>}
      {Object.keys(errors).length > 0 && <div className="inmate-config-errors" role="alert">
        <ul>{Object.values(errors).flat().map((message, index) => <li key={index}>{message}</li>)}</ul>
      </div>}
      <nav className="inmate-config-sections" aria-label="Configuration sections">
        {schema.sections.map(section => <a key={section.key} href={`#inmate-${section.key}`}>{section.label}</a>)}
      </nav>
      <fieldset disabled={busy}>
        <legend>Configuration details</legend>
        <label>Name
          <input required value={draft.name} maxLength={schema.nameMaxLength} aria-invalid={!!errors.name}
            onChange={event => edit(value => ({ ...value, name: event.target.value }))} />
        </label>
        <label>Description
          <input value={draft.description} maxLength={schema.descriptionMaxLength} aria-invalid={!!errors.description}
            onChange={event => edit(value => ({ ...value, description: event.target.value }))} />
        </label>
      </fieldset>
      {schema.sections.map(section => (
        <fieldset id={`inmate-${section.key}`} key={section.key} disabled={busy}>
          <legend>{section.label}</legend>
          {section.fields.map(field => <SettingControl key={field.key} field={field}
            value={draft.settings[field.key] === undefined ? field.defaultValue : draft.settings[field.key]}
            invalid={!!errors[`settings.${field.key}`]}
            onChange={setting => edit(value => ({ ...value, settings: { ...value.settings, [field.key]: setting } }))} />)}
        </fieldset>
      ))}
      <div className="row-actions">
        <button type="submit" disabled={busy}>Save configuration</button>
        {draft.id && <>
          <button className="quiet" type="button" disabled={busy}
            onClick={() => edit(value => ({ name: '', description: value.description, settings: value.settings }))}>Copy as new</button>
          <button className="quiet" type="button" disabled={busy} onClick={() => void remove()}>Delete configuration</button>
        </>}
      </div>
    </form>
  )
}

function SettingControl({ field, value, invalid, onChange }: {
  field: Field; value: Setting; invalid: boolean; onChange: (value: Setting) => void
}) {
  if (field.kind === 'checkbox') return <label className="inmate-config-check">
    <input type="checkbox" checked={value === true} aria-invalid={invalid} onChange={event => onChange(event.target.checked)} />
    {field.label}
  </label>

  return <label>
    {field.label}
    {field.kind === 'select' ? (
      <select value={String(value)} aria-invalid={invalid} onChange={event => onChange(Number(event.target.value))}>
        {field.choices?.map(choice => <option key={choice.value} value={choice.value}>{choice.label}</option>)}
      </select>
    ) : field.kind === 'number' ? (
      <input type="number" required value={value == null ? '' : String(value)} min={field.minimum ?? undefined}
        max={field.maximum ?? undefined} aria-invalid={invalid} step={1}
        onChange={event => onChange(event.target.value === '' ? null : event.target.valueAsNumber)} />
    ) : (
      <input value={value == null ? '' : String(value)} maxLength={field.maximum ?? undefined} aria-invalid={invalid}
        autoComplete="off" pattern="[0-9A-Da-d*#]*" onChange={event => onChange(event.target.value.toUpperCase())} />
    )}
  </label>
}

async function readFailure(response: Response): Promise<Failure> {
  const problem = await response.json().catch(() => null) as Failure | null
  return problem?.error || problem?.errors ? problem : { error: `Request failed (${response.status}).` }
}
