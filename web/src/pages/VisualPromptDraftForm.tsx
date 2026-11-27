import { useMemo, useState } from 'react'
import { authorableDisplayText } from './advertisingGlyphs'
import { VisualPromptWorksheet, type VisualPromptCompanion } from './VisualPromptWorksheet'
import { canonicalPromptText, stagePromptChanges, type PromptRow } from './visualPromptWorksheetCodec'

export type VisualPromptAuthoringSpec = { number: number; profile: string; length: number; rows: Omit<PromptRow, 'value'>[] }
export type VisualPromptAuthoringRequest = { profile: string; length: number; rows: Record<number, string> }

export function VisualPromptDraftForm({ spec, busy, companions, onCreate, onDirty }: {
  spec: VisualPromptAuthoringSpec; busy: boolean; onCreate: (request: VisualPromptAuthoringRequest) => void;
  companions?: VisualPromptCompanion[] | null;
  onDirty: (dirty: boolean) => void
}) {
  const rows = useMemo(() => spec.rows.map(row => ({ ...row, value: '' })), [spec.rows])
  const [edits, setEdits] = useState<Record<string, string>>({})
  const invalid = Object.values(edits).some(text => !authorableDisplayText(text, 20))
  const populated = Object.values(edits).filter(text => canonicalPromptText(text).length > 0).length
  function update(next: Record<string, string>) {
    setEdits(next)
    onDirty(Object.keys(next).length > 0)
  }

  return <form className="native-table-editor" aria-label="Create visual prompt record" onSubmit={event => {
    event.preventDefault()
    if (invalid || busy) return
    const authored = Object.fromEntries(rows.filter(row => Object.hasOwn(edits, row.key)).map(row => [row.row, canonicalPromptText(edits[row.key])]))
    onCreate({ profile: spec.profile, length: spec.length, rows: authored })
  }}>
    <h3>Author MT{spec.number} visual prompts</h3>
    <p>{spec.profile} · {spec.length.toLocaleString()} bytes · {rows.length} fixed rows. Enter text or import a reviewed worksheet.
      Every omitted row starts blank. Creating this record does not publish, assign or download it.</p>
    <VisualPromptWorksheet number={spec.number} rows={rows} edits={edits} editable={!busy} companions={companions}
      onStage={changes => update(stagePromptChanges(rows, edits, changes))}
      onUndo={key => { const next = { ...edits }; delete next[key]; update(next) }} />
    <p role="status">{populated} populated rows · {rows.length - populated} blank rows. Review missing or intentionally blank prompts before publishing.</p>
    <button type="submit" disabled={invalid || busy}>{busy ? 'Creating…' : populated === 0 ? 'Create blank prompt draft' : 'Create visual prompt record'}</button>
  </form>
}
