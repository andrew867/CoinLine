import { useState } from 'react'
import {
  defaultFieldPreferences, readFieldPreferences, saveFieldPreferences,
  type FieldHighlight, type FieldPreferences,
} from '../api/fieldPreferences'

export function FieldPreferencesPanel() {
  const [draft, setDraft] = useState(readFieldPreferences)
  const [notice, setNotice] = useState('')
  const [error, setError] = useState('')

  function change(kind: keyof FieldPreferences, update: Partial<FieldHighlight>) {
    setDraft(current => ({ ...current, [kind]: { ...current[kind], ...update } }))
    setNotice('')
    setError('')
  }

  function save(preferences: FieldPreferences) {
    try {
      saveFieldPreferences(preferences)
      setDraft(preferences)
      setNotice('Field highlighting saved for this browser.')
      setError('')
    } catch {
      setNotice('')
      setError('This browser could not save your workstation preferences.')
    }
  }

  return <section aria-labelledby="field-preferences-heading">
    <h2 id="field-preferences-heading">Field highlighting</h2>
    <p className="page-intro">Choose text and background colors for identifying record fields and mandatory information. These preferences apply to this browser, not to terminal data.</p>
    <div className="field-preferences-grid">
      {([
        ['key', 'Key fields', 'Configuration ID'],
        ['mandatory', 'Mandatory fields', 'Display name'],
      ] as const).map(([kind, label, example]) => {
        const value = draft[kind]
        return <fieldset key={kind} className="field-preference">
          <legend>{label}</legend>
          <label><input type="checkbox" checked={value.enabled}
            onChange={event => change(kind, { enabled: event.target.checked })} /> Highlight {label.toLowerCase()}</label>
          <div className="row-actions">
            <label>Text color
              <input type="color" aria-label={`${label} text color`} value={value.text}
                disabled={!value.enabled} onChange={event => change(kind, { text: event.target.value })} />
            </label>
            <label>Background color
              <input type="color" aria-label={`${label} background color`} value={value.background}
                disabled={!value.enabled} onChange={event => change(kind, { background: event.target.value })} />
            </label>
          </div>
          <p className="field-preference-preview" style={value.enabled ? { color: value.text, backgroundColor: value.background } : undefined}>
            {example}
          </p>
        </fieldset>
      })}
    </div>
    <div className="row-actions">
      <button type="button" onClick={() => save(draft)}>Save field highlighting</button>
      <button type="button" className="quiet" onClick={() => save(defaultFieldPreferences)}>Restore defaults</button>
    </div>
    {notice && <p role="status">{notice}</p>}
    {error && <p role="alert">{error}</p>}
  </section>
}
