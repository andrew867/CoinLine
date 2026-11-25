import { useState } from 'react'
import { previewInternational, previewNpa } from './regionalRatingLookup'

export function RegionalRatingPreview({ number, value, onOpen }: {
  number: 150 | 151
  value: (key: string) => string | null | undefined
  onOpen: (section: string) => void
}) {
  const [input, setInput] = useState('')
  const isNpa = number === 150
  const result = isNpa ? previewNpa(input, value) : previewInternational(input, value)
  const action = result.action.kind === 'tariff' ? `MT73 rate ${result.action.row}`
    : result.action.kind === 'external' ? 'External rating'
      : result.action.kind === 'reject' ? 'Reject the call' : 'Lookup unavailable'
  return <section className="table-config-panel" aria-label="Set-based rating preview">
    <h3>Set-based rating preview</h3>
    <p>Check which configured entry or fallback is selected, including unsaved edits. This is not a call quote: dialing rules, local tables, screening, feature switches, tariff contents and other call policy can change the final result.</p>
    <label>{isNpa ? 'Test area code' : 'Test international destination prefix'}
      <input value={input} onChange={event => setInput(event.target.value)} inputMode="numeric"
        autoComplete="off" maxLength={isNpa ? 3 : 32} />
    </label>
    {input && <div aria-live="polite">
      <p><strong>{action}</strong> | {result.source}{result.fallback ? ' | Configured fallback' : ''}</p>
      {!!result.duplicateSlots?.length && <p>Later matching entries ignored: {result.duplicateSlots.join(', ')}.</p>}
      {result.note && <p>{result.note}</p>}
      {result.section && <div className="row-actions">
        <button type="button" className="quiet" onClick={() => onOpen(result.section!)}>Open selected entry</button>
        {result.fallback && result.section !== 'default' &&
          <button type="button" className="quiet" onClick={() => onOpen('default')}>Open fallback settings</button>}
      </div>}
    </div>}
  </section>
}
