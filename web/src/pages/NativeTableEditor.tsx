import { useEffect, useState } from 'react'
import { NativeBitmapField } from './NativeBitmapField'

type Field = {
  key: string
  label: string
  editor: 'checkbox' | 'select' | 'text' | 'number' | 'bitmap' | 'password'
  value: string | null
  min: number
  max: number
  maxLength: number | null
  choices: { value: number; label: string }[] | null
  help: string | null
}

type Section = { key: string; label: string; fields: Field[]; showDisplayPreview?: boolean }
type Layout = {
  supported: true
  editable: boolean
  profile: string
  length: number
  revision: number
  sha256: string
  sections: Section[]
} | { supported: false; reason: string; profiles?: string[]; templates?: { profile: string; length: number }[] }

type Props = {
  setId: string
  number: number
  revision: number
  onSaved: () => void
  onDirty: (dirty: boolean) => void
}

export function NativeTableEditor({ setId, number, revision, onSaved, onDirty }: Props) {
  const [layout, setLayout] = useState<Layout | null>(null)
  const [sectionKey, setSectionKey] = useState('')
  const [search, setSearch] = useState('')
  const [edits, setEdits] = useState<Record<string, string>>({})
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const [reload, setReload] = useState(0)
  const [profile, setProfile] = useState('')
  const endpoint = `/api/tables/sets/${setId}/records/${number}/fields`

  useEffect(() => {
    const request = new AbortController()
    const url = profile ? `${endpoint}?profile=${encodeURIComponent(profile)}` : endpoint
    void fetch(url, { signal: request.signal }).then(async (response) => {
      if (!response.ok) throw new Error('Could not load this configuration record.')
      const result = await response.json() as Layout
      if (!request.signal.aborted) setLayout(result)
    }).catch((error: unknown) => {
      if (!request.signal.aborted) setMessage(error instanceof Error ? error.message : 'Could not load this record.')
    })
    return () => request.abort()
  }, [endpoint, revision, reload, profile])

  function edit(field: Field, value: string) {
    const updated = { ...edits }
    if (value === field.value || (field.editor === 'password' && value === '')) delete updated[field.key]
    else updated[field.key] = value
    setEdits(updated)
    onDirty(Object.keys(updated).length > 0)
    setMessage('')
  }

  async function save(event: React.FormEvent) {
    event.preventDefault()
    if (!layout?.supported || !layout.editable) return
    setBusy(true)
    setMessage('')
    try {
      const response = await fetch(endpoint, {
        method: 'PUT', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ revision: layout.revision, sha256: layout.sha256, changes: edits, profile: layout.profile }),
      })
      if (!response.ok) {
        const body = await response.json().catch(() => null)
        throw new Error(body?.error ?? 'Could not save this record.')
      }
      setEdits({})
      onDirty(false)
      setMessage('Changes saved to this draft.')
      setReload((count) => count + 1)
      onSaved()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Could not save this record.')
    } finally {
      setBusy(false)
    }
  }

  async function initialize(template: { profile: string; length: number }) {
    setBusy(true)
    setMessage('')
    try {
      const response = await fetch(endpoint, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(template),
      })
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.error ?? 'Could not create this record.')
      setReload((count) => count + 1)
      onSaved()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : 'Could not create this record.')
    } finally {
      setBusy(false)
    }
  }

  if (!layout) return <p role="status">{message || 'Loading record fields…'}</p>
  if (!layout.supported) return (
    <div className="native-table-editor">
      <p className="page-intro">{layout.reason}</p>
      {layout.profiles && layout.profiles.length > 1 && <label>Firmware profile
        <select value={profile} onChange={(event) => setProfile(event.target.value)}>
          <option value="">Select firmware profile</option>
          {layout.profiles.map((option) => <option key={option} value={option}>{option}</option>)}
        </select>
      </label>}
      {layout.templates?.map((template) => (
        <button key={`${template.profile}:${template.length}`} type="button" disabled={busy} onClick={() => void initialize(template)}>
          Start record for {template.profile}
        </button>
      ))}
      {message && <p role="status">{message}</p>}
    </div>
  )

  const needle = search.trim().toLowerCase()
  const sections = layout.sections.filter((section) => !needle || section.label.toLowerCase().includes(needle)
    || section.fields.some((field) => field.label.toLowerCase().includes(needle) || field.help?.toLowerCase().includes(needle)
      || (field.editor !== 'bitmap' && field.editor !== 'password'
        && (field.value?.toLowerCase().includes(needle) || edits[field.key]?.toLowerCase().includes(needle)))))
  const current = sections.find((section) => section.key === sectionKey) ?? sections[0]
  const changed = Object.keys(edits).length

  return (
    <form className="native-table-editor" onSubmit={(event) => void save(event)}>
      <header className="native-table-header">
        <div><strong>{layout.profile}</strong><span>{layout.length.toLocaleString()} bytes · Revision {layout.revision}</span></div>
        {!layout.editable && <span>Published record</span>}
      </header>
      {layout.sections.length > 1 && (
        <div className="table-config-selectors">
          {layout.sections.length > 12 && <label>Find entry<input type="search" value={search} onChange={(event) => setSearch(event.target.value)} /></label>}
          <label>Section or entry
            <select value={current?.key ?? ''} disabled={!current} onChange={(event) => setSectionKey(event.target.value)}>
              {!current && <option value="">No matching entries</option>}
              {sections.map((section) => <option key={section.key} value={section.key}>{section.label}</option>)}
            </select>
          </label>
        </div>
      )}
      {current && (
        <fieldset className="native-table-fields" disabled={!layout.editable || busy}>
          <legend>{current.label}</legend>
          {current.showDisplayPreview && (
            <figure className="native-table-prompt-preview">
              <figcaption>Terminal display preview</figcaption>
              <div className="table-config-vfd" role="img" aria-label={current.fields.map((field) => {
                const text = edits[field.key] ?? field.value
                return `${field.label}, ${text === null ? 'unmapped glyphs' : text.trim() || 'blank'}`
              }).join('. ')}>
                {current.fields.map((field) => (
                  <div key={field.key} aria-hidden="true">
                    {(edits[field.key] ?? field.value ?? 'Unmapped glyphs').padEnd(20)}
                  </div>
                ))}
              </div>
            </figure>
          )}
          {current.fields.map((field) => (
            <FieldInput key={field.key} field={field} value={edits[field.key] ?? field.value ?? ''}
              dirty={field.key in edits} onChange={(value) => edit(field, value)} />
          ))}
        </fieldset>
      )}
      {layout.editable && (
        <div className="row-actions">
          <button type="submit" disabled={changed === 0 || busy}>{busy ? 'Saving…' : 'Save record'}</button>
          {changed > 0 && <span>{changed} {changed === 1 ? 'field changed' : 'fields changed'}</span>}
        </div>
      )}
      {message && <p className="native-table-feedback" role="status">{message}</p>}
    </form>
  )
}

function FieldInput({ field, value, dirty, onChange }: {
  field: Field; value: string; dirty: boolean; onChange: (value: string) => void
}) {
  if (field.editor === 'bitmap') {
    return <NativeBitmapField name={field.label} value={field.value === null && !dirty ? null : value} onChange={onChange} />
  }
  if (field.editor === 'checkbox') {
    return <label className="native-table-toggle" data-dirty={dirty}>
      <input type="checkbox" checked={value === 'true'} onChange={(event) => onChange(String(event.target.checked))} />{field.label}
    </label>
  }

  const numeric = field.editor === 'number'
  const password = field.editor === 'password'
  return (
    <label className="native-table-field" data-dirty={dirty}>
      <span>{field.label}</span>
      {field.choices ? (
        <select value={value} onChange={(event) => onChange(event.target.value)}>
          {!field.choices.some((option) => String(option.value) === value) && <option value={value}>Stored value {value}</option>}
          {field.choices.map((option) => <option key={option.value} value={option.value}>{option.label}</option>)}
        </select>
      ) : (
        <input type={password ? 'password' : numeric ? 'number' : 'text'} value={value} min={numeric && dirty ? field.min : undefined}
          max={numeric && dirty ? field.max : undefined} step={numeric ? 1 : undefined}
          autoComplete={password ? 'new-password' : undefined} inputMode={password ? 'numeric' : undefined}
          pattern={password && field.maxLength ? `[0-9]{${field.maxLength}}` : undefined}
          maxLength={field.maxLength ?? undefined} required={numeric && dirty}
          onChange={(event) => onChange(event.target.value)} />
      )}
      {field.help && <small>{field.help}</small>}
      {field.value === null && !dirty && !password && <small>Stored characters are outside this editor’s format.</small>}
    </label>
  )
}
