import { useRef, useState } from 'react'
import { authorableDisplayText, westernDisplayGlyphs } from './advertisingGlyphs'
import { canonicalPromptText, makePromptWorksheet, maxPromptWorksheetBytes, planPromptImport, readPromptRow, type PromptImportPlan, type PromptRow } from './visualPromptWorksheetCodec'

const pageSize = 25

type PromptCoverage = 'blank' | 'populated' | 'unmapped'
export type VisualPromptCompanion = { number: number; status: 'compatible' | 'missing' | 'ambiguous' | 'incompatible'; rows: PromptCoverage[] }
const coverageLabels: Record<PromptCoverage, string> = { blank: 'Blank', populated: 'Text present', unmapped: 'Unmapped glyphs' }
const companionLabels = { compatible: 'Available', missing: 'Not in this set', ambiguous: 'Multiple records', incompatible: 'Incompatible record' }

export function VisualPromptWorksheet({ number, rows, edits, editable, companions, onStage, onUndo }: {
  number: number
  rows: PromptRow[]
  edits: Record<string, string>
  editable: boolean
  companions?: VisualPromptCompanion[] | null
  onStage: (changes: Record<string, string>) => void
  onUndo: (key: string) => void
}) {
  const [query, setQuery] = useState('')
  const [status, setStatus] = useState('all')
  const [page, setPage] = useState(0)
  const [json, setJson] = useState('')
  const [review, setReview] = useState<{ plan: PromptImportPlan; snapshot: string } | null>(null)
  const [error, setError] = useState('')
  const fileGeneration = useRef(0)
  const needle = query.trim().toLowerCase()
  const compatible = (companions ?? []).filter(companion => companion.status === 'compatible' && companion.rows.length === rows.length)
  const activeFilter = status === 'coverage' && compatible.length === 0 ? 'all' : status
  function differentCoverage(row: PromptRow) {
    const value = readPromptRow(row, edits)
    const current = value === null || !authorableDisplayText(value, 20) ? 'unmapped' : canonicalPromptText(value) ? 'populated' : 'blank'
    const coverage = [current, ...compatible.map(companion => companion.rows[row.row])]
    return coverage.includes('blank') && coverage.includes('populated')
  }
  const coverageDifferenceCount = rows.filter(differentCoverage).length
  const matches = rows.filter(row => {
    const value = readPromptRow(row, edits)
    return (activeFilter === 'all' || (activeFilter === 'changed' && Object.hasOwn(edits, row.key)) || (activeFilter === 'unmapped' && value === null)
      || (activeFilter === 'coverage' && differentCoverage(row)))
      && (!needle || row.label.toLowerCase().includes(needle) || String(row.row) === needle || value?.toLowerCase().includes(needle))
  })
  const pages = Math.max(1, Math.ceil(matches.length / pageSize))
  const shownPage = Math.min(page, pages - 1)
  const displayed = matches.slice(shownPage * pageSize, (shownPage + 1) * pageSize)
  const changedCount = rows.filter(row => Object.hasOwn(edits, row.key)).length
  const unmappedCount = rows.filter(row => readPromptRow(row, edits) === null).length
  const invalidCount = rows.filter(row => {
    const text = readPromptRow(row, edits)
    return text !== null && !authorableDisplayText(text, 20)
  }).length
  const reviewCurrent = review !== null && review.snapshot === JSON.stringify(edits)

  function download() {
    const url = URL.createObjectURL(new Blob([makePromptWorksheet(number, rows, edits)], { type: 'application/json;charset=utf-8' }))
    const link = document.createElement('a'); link.href = url; link.download = `coinline-mt${number}-visual-prompts.json`
    document.body.append(link); link.click(); link.remove()
    window.setTimeout(() => URL.revokeObjectURL(url), 1000)
  }

  function inspect() {
    setError(''); setReview(null)
    try { setReview({ plan: planPromptImport(json, rows, edits), snapshot: JSON.stringify(edits) }) }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'The worksheet could not be read.') }
  }

  function stage(changes: Record<string, string>) {
    setReview(null); setError(''); onStage(changes)
  }

  return <section className="visual-prompt-worksheet" aria-label="Visual prompt worksheet">
    <p className="page-intro">Fixed wire rows, 20 display cells each. Labels identify known message lines. Editing a row never inserts, removes or shifts another row.</p>
    {companions && <section className="visual-prompt-companions" aria-label="Companion language coverage">
      <h3>Companion language coverage</h3>
      <p>Compare the current rows, including staged edits, with saved rows in the same set. A blank line in one language can be intentional; this is a review aid, not a translation check or a publication restriction.</p>
      {companions.length ? <ul>{companions.map(companion => <li key={companion.number}><strong>MT{companion.number}</strong> · {
        companion.status === 'compatible' && companion.rows.length !== rows.length ? 'Incompatible record' : companionLabels[companion.status]}</li>)}</ul>
        : <p>No companion language records are catalogued for this target.</p>}
      {compatible.length > 0 && <p role="status">{coverageDifferenceCount} {coverageDifferenceCount === 1 ? 'row has' : 'rows have'} both blank and populated language entries.</p>}
    </section>}
    <div className="table-config-selectors">
      <label>Find prompt row<input type="search" value={query} onChange={event => { setQuery(event.target.value); setPage(0) }} /></label>
      <label>Show rows<select value={activeFilter} onChange={event => { setStatus(event.target.value); setPage(0) }}>
        <option value="all">All rows</option><option value="changed">Changed rows</option><option value="unmapped">Unmapped rows</option>
        {compatible.length > 0 && <option value="coverage">Blank in one language</option>}
      </select></label>
      <button type="button" className="quiet" disabled={invalidCount > 0} onClick={download}>Export worksheet JSON</button>
    </div>
    <p role="status">{rows.length} wire rows · {changedCount} staged · {unmappedCount} unmapped{invalidCount > 0 ? ` · ${invalidCount} invalid` : ''}</p>
    <div className="visual-prompt-grid">
      {displayed.map(row => {
        const value = readPromptRow(row, edits)
        const changed = Object.hasOwn(edits, row.key)
        const invalid = value !== null && !authorableDisplayText(value, 20)
        return <div className="visual-prompt-row" key={row.key}>
          <div><strong>Row {row.row}</strong><small>{row.label}</small>
            {compatible.length > 0 && <div className="visual-prompt-coverage">
              {compatible.map(companion => <span key={companion.number}>MT{companion.number}: {coverageLabels[companion.rows[row.row]]}</span>)}
              {differentCoverage(row) && <strong>Check language coverage</strong>}
            </div>}
          </div>
          {value === null ? <div className="visual-prompt-unmapped">
            <span>Unmapped glyphs, original bytes retained</span>
            {editable && <button type="button" className="quiet" onClick={() => stage({ [row.key]: '' })}>Replace row {row.row}</button>}
          </div> : <label className="visual-prompt-cell">
            <span className="visual-prompt-sr-only">Row {row.row} text</span>
            <input type="text" value={value} disabled={!editable} maxLength={40} aria-invalid={invalid}
              onChange={event => stage({ [row.key]: event.target.value })} />
            <small>{Array.from(value.normalize('NFC')).length}/20 cells{invalid ? ', unsupported or too long' : ''}</small>
          </label>}
          <div className="visual-prompt-row-actions">{changed && <span>Staged</span>}
            {changed && editable && <button type="button" className="quiet" onClick={() => { setReview(null); onUndo(row.key) }}>Undo row {row.row}</button>}
          </div>
        </div>
      })}
      {matches.length === 0 && <p>No matching prompt rows.</p>}
    </div>
    <div className="row-actions">
      <button type="button" className="quiet" disabled={shownPage === 0} onClick={() => setPage(shownPage - 1)}>Previous rows</button>
      <span>Page {shownPage + 1} of {pages}, {matches.length} matching rows</span>
      <button type="button" className="quiet" disabled={shownPage + 1 >= pages} onClick={() => setPage(shownPage + 1)}>Next rows</button>
    </div>
    {editable && <details className="visual-prompt-import">
      <summary>Import a worksheet or review supported glyphs</summary>
      <p>Supported additional Western glyphs: <code>{westernDisplayGlyphs}</code></p>
      <p>Import another worksheet of the same fixed layout, including another language slot. Null and omitted rows retain their current bytes; an explicit empty string clears a row. Review all changes before staging.</p>
      <label>Load worksheet JSON<input type="file" accept=".json,application/json" onChange={async event => {
        const generation = ++fileGeneration.current
        const file = event.currentTarget.files?.[0]
        event.currentTarget.value = ''
        if (!file) return
        setReview(null); setError('')
        if (file.size > maxPromptWorksheetBytes) { setError('The worksheet exceeds 256 KiB.'); return }
        try { const text = await file.text(); if (generation === fileGeneration.current) setJson(text) }
        catch { if (generation === fileGeneration.current) setError('Could not read the worksheet file.') }
      }} /></label>
      <label>Worksheet JSON<textarea rows={5} value={json} onChange={event => { fileGeneration.current++; setJson(event.target.value); setReview(null); setError('') }} /></label>
      <button type="button" className="quiet" disabled={!json.trim()} onClick={inspect}>Review worksheet import</button>
      {error && <p role="alert">{error}</p>}
      {review && <div className="visual-prompt-review" aria-label="Worksheet import review">
        <h3>Import review</h3>
        <p>MT{review.plan.recordNumber} to MT{number}: {review.plan.changes.length} text changes, {review.plan.retainedRows + review.plan.missingRows} retained rows.</p>
        {!reviewCurrent && <p role="alert">Staged edits changed. Review the import again.</p>}
        {review.plan.changes.length > 0 && <ul>{review.plan.changes.map(change => <li key={change.key}>
          <strong>Row {change.row}</strong> · {change.label}<br />
          <span>{change.before === null ? 'Unmapped glyphs' : change.before || '(blank)'}</span> → <span>{change.text || '(blank)'}</span>
        </li>)}</ul>}
        <button type="button" disabled={!reviewCurrent || !review.plan.changes.length}
          onClick={() => stage(Object.fromEntries(review.plan.changes.map(change => [change.key, change.text])))}>Stage reviewed rows</button>
      </div>}
    </details>}
  </section>
}
