import { authorableDisplayText } from './advertisingGlyphs'

export const promptWorksheetFormat = 'coinline-visual-prompts-v1'
export const maxPromptWorksheetBytes = 256 * 1024

export type PromptRow = {
  row: number
  key: string
  label: string
  value: string | null
}

export function visualPromptRowCount(number: number, length: number) {
  if ((number === 27 || number === 28) && length === 2801) return 140
  if ([85, 86, 98, 99].includes(number) && length === 8001) return 400
  return null
}

export function promptRows(sections: { label: string; fields: { key: string; label: string; value: string | null; displayGlyphs?: boolean; maxLength: number | null }[] }[], count: number) {
  const rows: PromptRow[] = []
  for (const section of sections) {
    for (const field of section.fields) {
      const match = /^prompt-(\d+)\.text$/.exec(field.key)
      if (!match || !field.displayGlyphs || field.maxLength !== 20) return null
      rows.push({ row: Number(match[1]), key: field.key, label: `${section.label} · ${field.label}`, value: field.value })
    }
  }
  rows.sort((a, b) => a.row - b.row)
  return rows.length === count && rows.every((row, index) => row.row === index) ? rows : null
}

export function canonicalPromptText(text: string) {
  return text.normalize('NFC').replace(/ +$/, '')
}

export function readPromptRow(row: PromptRow, edits: Record<string, string>) {
  return Object.hasOwn(edits, row.key) ? edits[row.key] : row.value
}

export function makePromptWorksheet(number: number, rows: PromptRow[], edits: Record<string, string>) {
  return JSON.stringify({ format: promptWorksheetFormat, recordNumber: number, rowCount: rows.length,
    rows: rows.map(row => ({ row: row.row, label: row.label, text: readPromptRow(row, edits) })) }, null, 2)
}

export type PromptImportPlan = {
  recordNumber: number
  missingRows: number
  retainedRows: number
  changes: { row: number; key: string; label: string; before: string | null; text: string }[]
}

export function planPromptImport(json: string, rows: PromptRow[], edits: Record<string, string>): PromptImportPlan {
  if (json.length > maxPromptWorksheetBytes || new TextEncoder().encode(json).length > maxPromptWorksheetBytes)
    throw new Error('The worksheet exceeds 256 KiB.')
  let document: unknown
  try { document = JSON.parse(json.replace(/^\uFEFF/, '')) }
  catch { throw new Error('The worksheet is not valid JSON.') }
  if (typeof document !== 'object' || document === null || Array.isArray(document)) throw new Error('Choose a CoinLine visual-prompt worksheet.')
  const file = document as Record<string, unknown>
  if (file.format !== promptWorksheetFormat || typeof file.recordNumber !== 'number'
      || visualPromptRowCount(file.recordNumber, rows.length * 20 + 1) !== rows.length
      || file.rowCount !== rows.length || !Array.isArray(file.rows))
    throw new Error('The worksheet does not match this terminal prompt layout.')
  if (file.rows.length > rows.length) throw new Error('The worksheet contains too many rows.')
  const seen = new Set<number>()
  const changes: PromptImportPlan['changes'] = []
  let retainedRows = 0
  for (const entry of file.rows) {
    if (typeof entry !== 'object' || entry === null || Array.isArray(entry)) throw new Error('Each worksheet row must identify its wire row and text.')
    const item = entry as Record<string, unknown>
    const index = item.row
    if (typeof index !== 'number' || !Number.isInteger(index) || index < 0 || index >= rows.length || seen.has(index))
      throw new Error('Every wire row must be unique and within the selected layout.')
    seen.add(index)
    if (item.text === null) { retainedRows++; continue }
    if (typeof item.text !== 'string' || !authorableDisplayText(item.text, 20))
      throw new Error(`Wire row ${index} must contain up to 20 supported display cells, or null to retain it.`)
    const row = rows[index]
    const before = readPromptRow(row, edits)
    const text = canonicalPromptText(item.text)
    if (before !== null && canonicalPromptText(before) === text) { retainedRows++; continue }
    changes.push({ row: index, key: row.key, label: row.label, before, text })
  }
  return { recordNumber: file.recordNumber, missingRows: rows.length - seen.size, retainedRows, changes: changes.sort((a, b) => a.row - b.row) }
}

export function stagePromptChanges(rows: PromptRow[], edits: Record<string, string>, changes: Record<string, string>) {
  const updated = { ...edits }
  for (const row of rows) {
    if (!Object.hasOwn(changes, row.key)) continue
    const text = changes[row.key]
    if (row.value !== null && text === row.value) delete updated[row.key]
    else updated[row.key] = text
  }
  return updated
}
