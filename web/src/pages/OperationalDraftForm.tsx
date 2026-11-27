import { useEffect, useState } from 'react'
import { apiFetch, apiPostRaw } from '../api/client'
import { authorableDisplayText } from './advertisingGlyphs'
import { NumberingPlanEditor, numberingRuleBytes, type NumberingPlanChanges } from './NumberingPlanEditor'

type Field = { key: string; label: string; editor: 'number' | 'select' | 'checkbox' | 'text' | 'password'; min: number; max: number;
  choices: { value: number; label: string }[] | null; help: string | null; maxLength?: number | null; displayGlyphs?: boolean }
export type OperationalSpec = { number: number; name: string; profile: string; length: number;
  sections: { key: string; label: string; fields: Field[] }[]; numberingFormatCapacity?: number | null }
type CreateRequest = { profile: string; length: number; fields: Record<string, string>; credentials?: Record<string, string>; numberingPlan?: NumberingPlanChanges }
type Values = Record<string, string>

function validValue(field: Field, value?: string) {
  if (field.editor === 'text') return value != null && (field.displayGlyphs ? authorableDisplayText(value, field.maxLength ?? 0)
    : value.length <= (field.maxLength ?? 0) && /^[0-9*#]*$/.test(value))
  if (field.editor === 'password') return value != null && value.length === field.maxLength && /^[0-9]+$/.test(value)
  if (value == null || value === '') return false
  if (field.editor === 'checkbox') return value === 'true' || value === 'false'
  if (!/^-?\d+$/.test(value)) return false
  const number = Number(value)
  return Number.isSafeInteger(number) && number >= field.min && number <= field.max &&
    (field.choices == null || field.choices.some(choice => choice.value === number))
}

export function OperationalDraftForm({ templates, busy, onCreate, onDirty }: {
  templates: OperationalSpec[]; busy: boolean; onCreate: (request: CreateRequest) => void; onDirty: (dirty: boolean) => void
}) {
  const [profile, setProfile] = useState(templates.length === 1 ? templates[0].profile : '')
  const [sectionKey, setSectionKey] = useState('')
  const [byProfile, setByProfile] = useState<Record<string, Values>>({})
  const [rulesByProfile, setRulesByProfile] = useState<Record<string, NumberingPlanChanges>>({})
  const [json, setJson] = useState('')
  const [review, setReview] = useState<Values | null>(null)
  const [error, setError] = useState('')
  const spec = templates.find(template => template.profile === profile)
  const values = byProfile[profile] ?? {}
  const fields = spec?.sections.flatMap(section => section.fields) ?? []
  const section = spec?.sections.find(row => row.key === sectionKey) ?? spec?.sections[0]
  const rules = rulesByProfile[profile] ?? {}
  const formatCapacity = spec?.numberingFormatCapacity
  const rulesComplete = formatCapacity == null || (!!rules.dial?.length && rules.format != null
    && numberingRuleBytes(rules.dial, true) <= 180 && numberingRuleBytes(rules.format, false) <= formatCapacity)
  const complete = fields.filter(field => validValue(field, values[field.key])).length
  const canCreate = !!spec && fields.length > 0 && complete === fields.length && rulesComplete
  const unenteredSwitches = fields.filter(field => field.editor === 'checkbox' && !values[field.key])

  function update(next: Values) {
    const all = { ...byProfile, [profile]: next }
    setByProfile(all); setReview(null); setError('')
    onDirty(Object.values(all).some(row => Object.keys(row).length > 0) || Object.values(rulesByProfile).some(row => Object.keys(row).length > 0) || !!json.trim())
  }

  function updateRules(group: 'dial' | 'format', next: NonNullable<NumberingPlanChanges[typeof group]>) {
    setRulesByProfile({ ...rulesByProfile, [profile]: { ...rules, [group]: next } })
    onDirty(true)
  }

  function reviewValues() {
    setReview(null); setError('')
    try {
      if (json.length > 64 * 1024) throw new Error('The settings JSON exceeds 64 KiB.')
      const parsed = JSON.parse(json) as unknown
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('Enter a JSON object keyed by the named settings.')
      const next: Values = {}
      for (const [key, raw] of Object.entries(parsed)) {
        const field = fields.find(item => item.key === key)
        if (!field) throw new Error('Unknown setting in named settings JSON.')
        if (field.editor === 'password') {
          setJson('')
          throw new Error('Protected configuration values must be entered in their separate fields. The pasted JSON has been cleared.')
        }
        if (field.editor === 'text' && typeof raw !== 'string') throw new Error(`Use a quoted string for ${field.label}.`)
        const value = typeof raw === 'string' || typeof raw === 'number' || typeof raw === 'boolean' ? String(raw) : ''
        if (!validValue(field, value)) throw new Error(`Invalid value for ${field.label}.`)
        next[key] = value
      }
      if (!Object.keys(next).length) throw new Error('Enter at least one named setting.')
      setReview(next)
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'Could not review the named settings.') }
  }

  return <form className="native-table-editor" aria-label="Create operational record" onSubmit={event => {
    event.preventDefault(); if (!spec || !canCreate || busy) return
    const credentials = fields.filter(field => field.editor === 'password')
    onCreate({ profile: spec.profile, length: spec.length,
      fields: Object.fromEntries(fields.filter(field => field.editor !== 'password').map(field => [field.key, values[field.key]])),
      ...(credentials.length ? { credentials: Object.fromEntries(credentials.map(field => [field.key, values[field.key]])) } : {}),
      ...(formatCapacity != null ? { numberingPlan: rules } : {}) })
  }}>
    <h3>Author {spec ? `MT${spec.number} ${spec.name}` : 'operational settings'}</h3>
    <p>You can author this record directly instead of importing a binary. Enter the known settings for the actual target.
      No operating defaults are supplied. New spare and unassigned bits start at zero;
      the saved native editor allows separate spare-field review. Creating the record does not publish, assign or download it.</p>
    <label>Operational layout<select value={profile} disabled={busy || templates.length === 1} onChange={event => {
      setProfile(event.target.value); setSectionKey(''); setReview(null); setError('')
    }}>
      {!spec && <option value="">Choose the matching layout</option>}
      {templates.map(template => <option key={template.profile} value={template.profile}>{template.profile}, {template.length} bytes</option>)}
    </select></label>
    {spec && section && <>
      <p role="status">{complete} of {fields.length} settings assigned. {fields.length - complete} still need a valid value.</p>
      <div className="table-config-selectors">
        <label>Settings section<select value={section.key} onChange={event => setSectionKey(event.target.value)} disabled={busy}>
          {spec.sections.map(row => <option key={row.key} value={row.key}>{row.label} ({row.fields.filter(field => validValue(field, values[field.key])).length}/{row.fields.length})</option>)}
        </select></label>
        {fields.some(field => field.editor === 'checkbox') && <button type="button" className="quiet" disabled={busy || !unenteredSwitches.length}
          onClick={() => update({ ...values, ...Object.fromEntries(unenteredSwitches.map(field => [field.key, 'false'])) })}>Set unentered switches to Off</button>}
      </div>
      <fieldset className="native-table-fields" disabled={busy}>
        <legend>{section.label}</legend>
        {section.fields.map(field => field.editor === 'text' ? <div className="native-table-field" key={field.key}>
          <label>{field.label}<input type="text" maxLength={field.maxLength ?? undefined} value={values[field.key] ?? ''}
            aria-invalid={values[field.key] !== undefined && !validValue(field, values[field.key])}
            onChange={event => update({ ...values, [field.key]: event.target.value })} /></label>
          <button type="button" className="quiet" aria-label={`${field.displayGlyphs ? 'Use no text' : 'Use no digits'} for ${field.label}`}
            onClick={() => update({ ...values, [field.key]: '' })}>{field.displayGlyphs ? 'Use no text' : 'Use no digits'}</button>
          <small>{field.key}{values[field.key] === '' ? ' · Explicitly blank' : values[field.key] === undefined ? ' · Not yet selected' : ''}{field.help ? ` · ${field.help}` : ''}</small>
        </div> : <label className="native-table-field" key={field.key}>
          <span>{field.label}</span>
          {field.editor === 'checkbox' || field.choices ? <select value={values[field.key] ?? ''}
            onChange={event => update({ ...values, [field.key]: event.target.value })}>
            <option value="">Choose a value</option>
            {field.editor === 'checkbox' ? <><option value="false">Off</option><option value="true">On</option></>
              : field.choices?.map(choice => <option key={choice.value} value={choice.value}>{choice.label}</option>)}
          </select> : field.editor === 'password' ? <input type="password" inputMode="numeric" autoComplete="new-password"
            maxLength={field.maxLength ?? undefined} value={values[field.key] ?? ''}
            aria-invalid={!!values[field.key] && !validValue(field, values[field.key])}
            onChange={event => update({ ...values, [field.key]: event.target.value })} />
          : <input type="number" step={1} min={field.min} max={field.max} value={values[field.key] ?? ''}
            aria-invalid={!!values[field.key] && !validValue(field, values[field.key])}
            onChange={event => update({ ...values, [field.key]: event.target.value })} />}
          <small>{field.key}{field.help ? ` · ${field.help}` : ''}</small>
        </label>)}
      </fieldset>
      <details><summary>Import reviewed named settings</summary>
        <p>Paste a JSON object using the field names shown above. Boolean switches accept true or false, numeric entries accept whole numbers,
          and digit strings must be quoted, including an explicitly empty string. Review the supplied values before applying them; unspecified settings stay as entered.
          {fields.some(field => field.editor === 'password') && ' Enter protected configuration values only in their separate password fields, never in this JSON.'}</p>
        <label>Named settings JSON<textarea rows={5} value={json} disabled={busy} onChange={event => {
          const next = event.target.value; setJson(next); setReview(null); setError('');
          onDirty(Object.values(byProfile).some(row => Object.keys(row).length > 0) || Object.values(rulesByProfile).some(row => Object.keys(row).length > 0) || !!next.trim())
        }} /></label>
        <button type="button" className="quiet" disabled={busy || !json.trim()} onClick={reviewValues}>Review named settings</button>
        {error && <p role="alert">{error}</p>}
        {review && <section aria-label="Named settings review">
          <p>{Object.keys(review).length} reviewed settings; {Object.keys(review).filter(key => values[key] !== undefined && values[key] !== review[key]).length} replace entered values.</p>
          <ul>{Object.entries(review).map(([key, value]) => <li key={key}>{fields.find(field => field.key === key)?.label}: {value}</li>)}</ul>
          <button type="button" disabled={busy} onClick={() => update({ ...values, ...review })}>Apply reviewed settings</button>
        </section>}
      </details>
      {formatCapacity != null && <section aria-label="New numbering-plan rules">
        <h4>Dialing and display-format rules</h4>
        <p>Author the complete linked dialing graph. Display formatting may be deliberately empty; it is not assumed.
          The earlier and later profiles have different formatting capacities.</p>
        <p role="status">{rules.dial?.length ?? 0} dialing rules; {rules.format == null ? 'display formatting not yet chosen' : `${rules.format.length} display-format rules`}.</p>
        {rules.format == null && <button type="button" className="quiet" disabled={busy} onClick={() => updateRules('format', [])}>Leave display formatting empty</button>}
        <NumberingPlanEditor key={profile} plan={{ dial: [], format: [] }} changes={rules} editable={!busy}
          formatCapacity={formatCapacity} onChange={updateRules} />
      </section>}
    </>}
    <button type="submit" disabled={!canCreate || busy}>{busy ? 'Creating…' : 'Create operational record'}</button>
  </form>
}

export function OperationalDraftLoader({ setId, number, onCreated, onDirty }: {
  setId: string; number: number; onCreated: (profile: string) => void; onDirty: (dirty: boolean) => void
}) {
  const [templates, setTemplates] = useState<OperationalSpec[] | null>(null)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const endpoint = `/api/tables/sets/${setId}/records/${number}/operational-authoring`
  useEffect(() => {
    const request = new AbortController()
    void apiFetch(endpoint, { signal: request.signal }).then(async response => {
      if (!response.ok) throw new Error('Could not load operational authoring options.')
      const result = await response.json() as { templates: OperationalSpec[] }
      if (!request.signal.aborted) setTemplates(result.templates ?? [])
    }).catch((cause: unknown) => { if (!request.signal.aborted) setError(cause instanceof Error ? cause.message : 'Could not load operational authoring options.') })
    return () => request.abort()
  }, [endpoint])
  async function create(request: CreateRequest) {
    if (busy) return
    setBusy(true); setError('')
    try {
      const response = await apiPostRaw(endpoint, request)
      if (!response.ok) throw new Error((await response.json().catch(() => null))?.error ?? 'Could not create the operational record.')
      onDirty(false); onCreated(request.profile)
    } catch (cause) { setError(cause instanceof Error ? cause.message : 'Could not create the operational record.') }
    finally { setBusy(false) }
  }
  return <>
    {templates == null && !error && <p role="status">Checking operational authoring layouts…</p>}
    {templates?.length === 0 && <p>No confirmed authoring layout is available for this target. Import an independently reviewed compatible record.</p>}
    {templates && templates.length > 0 && <OperationalDraftForm templates={templates} busy={busy}
      onCreate={request => void create(request)} onDirty={onDirty} />}
    {error && <p role="alert">{error}</p>}
  </>
}
