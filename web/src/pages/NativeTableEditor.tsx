import { useEffect, useState } from 'react'
import { NativeBitmapField } from './NativeBitmapField'
import { GraphicalFlashField, GraphicalLinkField, GraphicalPanelPreview, GraphicalTextField } from './NativeGraphicalFields'
import { NumberingPlanEditor, type NumberingPlan, type NumberingPlanChanges } from './NumberingPlanEditor'

type Field = {
  key: string
  label: string
  editor: 'checkbox' | 'select' | 'text' | 'number' | 'bitmap' | 'password'
    | 'screen-kind' | 'screen-text' | 'screen-link' | 'flash-rate' | 'screen-icon' | 'card-standard'
  value: string | null
  min: number
  max: number
  maxLength: number | null
  choices: { value: number; label: string }[] | null
  help: string | null
}

type Section = {
  key: string; label: string; fields: Field[]; showDisplayPreview?: boolean
  graphicalScreenIndex?: number | null; showPanelPreview?: boolean
}
type Layout = {
  supported: true
  editable: boolean
  profile: string
  profiles?: string[]
  length: number
  revision: number
  sha256: string
  sections: Section[]
  numberingPlan?: NumberingPlan | null
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
  const [ruleEdits, setRuleEdits] = useState<NumberingPlanChanges>({})
  const [view, setView] = useState<'settings' | 'rules'>('settings')
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
    if (field.editor === 'screen-kind' && value !== field.value && layout?.supported) {
      const screen = layout.sections.find((section) => section.fields.includes(field))?.graphicalScreenIndex
      const others = layout.sections.filter((section) => section.graphicalScreenIndex === screen)
        .flatMap((section) => section.fields).filter((item) => item.key !== field.key)
      if ((field.value !== '0' || others.some((item) => item.key in updated)) &&
          !window.confirm('Changing the screen type replaces its content and clears pending edits for this screen. Continue?')) return
      for (const item of others) delete updated[item.key]
    }
    if (field.editor === 'card-standard' && layout?.supported && cardDataKind(value) !== cardDataKind(edits[field.key] ?? field.value)) {
      if (cardDataKind(value) !== cardDataKind(field.value)
          && !window.confirm('Changing card families clears the stored service-code or stored-value data for this range. Continue?')) return
      const prefix = field.key.replace(/\.format$/, '-')
      for (const section of layout.sections.filter((section) => section.key.startsWith(prefix))) {
        for (const item of section.fields) delete updated[item.key]
      }
    }
    if (value === field.value || (field.editor === 'password' && value === '')) delete updated[field.key]
    else updated[field.key] = value
    setEdits(updated)
    onDirty(Object.keys(updated).length > 0 || Object.keys(ruleEdits).length > 0)
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
        body: JSON.stringify({ revision: layout.revision, sha256: layout.sha256, changes: edits, profile: layout.profile,
          numberingPlan: Object.keys(ruleEdits).length ? ruleEdits : undefined }),
      })
      if (!response.ok) {
        const body = await response.json().catch(() => null)
        throw new Error(body?.error ?? 'Could not save this record.')
      }
      setEdits({})
      setRuleEdits({})
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

  function changeProfile(value: string) {
    if ((Object.keys(edits).length > 0 || Object.keys(ruleEdits).length > 0)
      && !window.confirm('Discard the unsaved changes and switch firmware profiles?')) return
    setEdits({})
    setRuleEdits({})
    setView('settings')
    onDirty(false)
    setSectionKey('')
    setSearch('')
    setMessage('')
    setProfile(value)
    setLayout(null)
  }

  if (!layout) return <p role="status">{message || 'Loading record fields…'}</p>
  if (!layout.supported) return (
    <div className="native-table-editor">
      <p className="page-intro">{layout.reason}</p>
      {layout.profiles && layout.profiles.length > 1 && <label>Firmware profile
        <select value={profile} onChange={(event) => changeProfile(event.target.value)}>
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
  const settings = layout.sections.filter((section) => !layout.numberingPlan?.dial || !section.key.startsWith('dial-'))
  const sections = settings.filter((section) => !needle || section.label.toLowerCase().includes(needle)
    || section.fields.some((field) => field.label.toLowerCase().includes(needle) || field.help?.toLowerCase().includes(needle)
      || (field.editor !== 'bitmap' && field.editor !== 'password'
        && (field.value?.toLowerCase().includes(needle) || edits[field.key]?.toLowerCase().includes(needle)))))
  const current = sections.find((section) => section.key === sectionKey) ?? sections[0]
  const changed = Object.keys(edits).length
  const ruleChanges = Object.keys(ruleEdits).length > 0
  const showingRules = layout.numberingPlan && view === 'rules'
  const changingKind = current?.graphicalScreenIndex != null && `screen-${current.graphicalScreenIndex}.kind` in edits
  const cardMainSection = current?.key.match(/^card-\d+(?=-(?:service|spill|stored-value)$)/)?.[0]
  const cardStandard = layout.sections.find((section) => section.key === cardMainSection)?.fields
    .find((field) => field.editor === 'card-standard')
  const changingCardFamily = cardStandard && cardDataKind(edits[cardStandard.key] ?? cardStandard.value) !== cardDataKind(cardStandard.value)
  const effectiveValue = (field: Field) => edits[field.key] ?? field.value

  return (
    <form className="native-table-editor" onSubmit={(event) => void save(event)}>
      <header className="native-table-header">
        <div><strong>{layout.profile}</strong><span>{layout.length.toLocaleString()} bytes · Revision {layout.revision}</span></div>
        {!layout.editable && <span>Published record</span>}
      </header>
      {layout.profiles && layout.profiles.length > 1 && (
        <div className="table-config-selectors">
          <label>Firmware profile
            <select value={layout.profile} disabled={busy} onChange={(event) => changeProfile(event.target.value)}>
              {layout.profiles.map((option) => <option key={option} value={option}>{option}</option>)}
            </select>
          </label>
        </div>
      )}
      {layout.numberingPlan && <div className="row-actions" role="group" aria-label="Numbering-plan editor">
        <button type="button" aria-pressed={!showingRules} onClick={() => setView('settings')}>Settings</button>
        <button type="button" aria-pressed={!!showingRules} onClick={() => setView('rules')}>Numbering rules</button>
      </div>}
      {showingRules && layout.numberingPlan && <NumberingPlanEditor plan={layout.numberingPlan} changes={ruleEdits}
        editable={layout.editable && !busy} onChange={(group, rules) => {
          const updated = { ...ruleEdits }
          if (JSON.stringify(rules) === JSON.stringify(layout.numberingPlan?.[group])) delete updated[group]
          else updated[group] = rules
          setRuleEdits(updated)
          onDirty(changed > 0 || Object.keys(updated).length > 0)
          setMessage('')
        }} />}
      {!showingRules && settings.length > 1 && (
        <div className="table-config-selectors">
          {settings.length > 12 && <label>Find entry<input type="search" value={search} onChange={(event) => setSearch(event.target.value)} /></label>}
          <label>Section or entry
            <select value={current?.key ?? ''} disabled={!current} onChange={(event) => setSectionKey(event.target.value)}>
              {!current && <option value="">No matching entries</option>}
              {sections.map((section) => <option key={section.key} value={section.key}>{section.label}</option>)}
            </select>
          </label>
        </div>
      )}
      {!showingRules && current && (
        <fieldset className="native-table-fields" disabled={!layout.editable || busy}>
          <legend>{current.label}</legend>
          {changingKind && <p className="native-graphical-status">Save the screen type to load its editor.</p>}
          {changingCardFamily && <p className="native-graphical-status">Save the card standard to load its settings.</p>}
          {!changingKind && current.showPanelPreview && (
            <GraphicalPanelPreview text={current.fields.slice(0, 4).map(effectiveValue)} keys={current.fields.slice(4, 10).map(effectiveValue)} />
          )}
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
          {!changingCardFamily && current.fields.filter((field) => !changingKind || field.editor === 'screen-kind').map((field) => (
            <FieldInput key={field.key} field={field} value={edits[field.key] ?? field.value ?? ''}
              dirty={field.key in edits} onChange={(value) => edit(field, value)} />
          ))}
        </fieldset>
      )}
      {layout.editable && (
        <div className="row-actions">
          <button type="submit" disabled={(changed === 0 && !ruleChanges) || busy}>{busy ? 'Saving…' : 'Save record'}</button>
          {changed > 0 && <span>{changed} {changed === 1 ? 'field changed' : 'fields changed'}</span>}
          {ruleChanges && <span>Numbering rules changed</span>}
        </div>
      )}
      {message && <p className="native-table-feedback" role="status">{message}</p>}
    </form>
  )
}

function cardDataKind(standard: string | null) {
  const code = standard === null ? 255 : Number(standard)
  return code >= 1 && code <= 9 ? 'magnetic' : code >= 10 && code <= 16 ? 'stored-value' : 'unknown'
}

function FieldInput({ field, value, dirty, onChange }: {
  field: Field; value: string; dirty: boolean; onChange: (value: string) => void
}) {
  if (field.editor === 'bitmap') {
    return <NativeBitmapField name={field.label} value={field.value === null && !dirty ? null : value} onChange={onChange} />
  }
  if (field.editor === 'screen-text') {
    return <GraphicalTextField label={field.label} value={value} unmapped={field.value === null && !dirty} onChange={onChange} />
  }
  if (field.editor === 'screen-link') return <GraphicalLinkField label={field.label} value={value} onChange={onChange} />
  if (field.editor === 'flash-rate') return <GraphicalFlashField label={field.label} value={value} onChange={onChange} />
  if (field.editor === 'screen-icon') {
    const packed = Number(value)
    return <label className="native-table-field" data-dirty={dirty}>
      <span>{field.label}</span>
      <select value={packed & 63} onChange={(event) => onChange(String((packed & 192) | Number(event.target.value)))}>
        {Array.from({ length: 64 }, (_, index) => <option key={index} value={index}>{index}</option>)}
      </select>
    </label>
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
