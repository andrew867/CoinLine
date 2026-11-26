import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'
import { makePromptWorksheet, planPromptImport, promptRows, promptWorksheetFormat, stagePromptChanges, visualPromptRowCount } from './visualPromptWorksheetCodec'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

const sections = (count = 140) => Array.from({ length: count / 2 }, (_, index) => ({
  key: `message-${index}`, label: `Message ${index}`, showDisplayPreview: true,
  fields: [index * 2, index * 2 + 1].map(row => ({
    key: `prompt-${row}.text`, label: row % 2 === 0 ? 'Upper line' : 'Lower line',
    value: row === 1 ? null : row === 0 ? 'Café' : '', editor: 'text', displayGlyphs: true,
    min: 0, max: 0, maxLength: 20, choices: null, help: `Wire row ${row}.`,
  })),
}))
const rows = promptRows(sections(), 140)!
const document = (entries: unknown[], number = 28, rowCount = 140) => JSON.stringify({
  format: promptWorksheetFormat, recordNumber: number, rowCount, rows: entries,
})

describe('visual prompt worksheet codec', () => {
  it('recognizes exact fixed layouts and rejects incomplete or duplicated field maps', () => {
    expect(visualPromptRowCount(27, 2801)).toBe(140)
    expect(visualPromptRowCount(99, 8001)).toBe(400)
    expect(visualPromptRowCount(27, 8001)).toBeNull()
    expect(visualPromptRowCount(29, 2801)).toBeNull()
    expect(promptRows(sections(400), 400)?.at(-1)?.row).toBe(399)
    expect(promptRows(sections().slice(1), 140)).toBeNull()
    expect(promptRows([...sections().slice(1), sections()[1]], 140)).toBeNull()
  })

  it('round trips all rows without changing padding, normalizes accents, and preserves opaque and omitted text', () => {
    const exported = JSON.parse(makePromptWorksheet(27, rows, {}))
    expect(exported.rows[1]).toEqual({ row: 1, label: 'Message 0 · Lower line', text: null })
    expect(planPromptImport(JSON.stringify(exported), rows, {}).changes).toEqual([])
    const plan = planPromptImport(document([{ row: 0, text: 'Cafe\u0301 ' }, { row: 1, text: null }, { row: 139, text: 'Fin' }]), rows, {})
    expect(plan.recordNumber).toBe(28)
    expect(plan.retainedRows).toBe(2)
    expect(plan.missingRows).toBe(137)
    expect(plan.changes).toEqual([{ row: 139, key: 'prompt-139.text', label: 'Message 69 · Lower line', before: '', text: 'Fin' }])
    expect(stagePromptChanges(rows, { 'prompt-4.text': 'Draft' }, { 'prompt-139.text': 'Fin' })).toEqual({ 'prompt-4.text': 'Draft', 'prompt-139.text': 'Fin' })
  })

  it('distinguishes explicit clearing from leaving an unmapped row intact and rejects invalid documents atomically', () => {
    const plan = planPromptImport(document([{ row: 1, text: '' }]), rows, {})
    expect(plan.changes[0]).toMatchObject({ row: 1, before: null, text: '' })
    expect(stagePromptChanges(rows, {}, { 'prompt-1.text': '' })).toEqual({ 'prompt-1.text': '' })
    expect(stagePromptChanges(rows, { 'prompt-0.text': 'Changed' }, { 'prompt-0.text': 'Café' })).toEqual({})
    expect(stagePromptChanges(rows, {}, { 'prompt-0.text': 'Café ' })).toEqual({ 'prompt-0.text': 'Café ' })
    for (const bad of [
      document([{ row: 2, text: 'Valid' }, { row: 2, text: 'Duplicate' }]),
      document([{ row: 140, text: 'Out of range' }]), document([{ row: -1, text: 'Invalid' }]),
      document([{ row: 1, text: 'a'.repeat(21) }]), document([{ row: 1, text: '漢' }]),
      document([{ row: 1, text: 'line\nbreak' }]), document([{ row: 1 }]),
      document([{ row: 1, text: 'Valid' }], 85, 140), 'not JSON', ' '.repeat(270_000),
    ]) expect(() => planPromptImport(bad, rows, {})).toThrow()
  })
})

function renderEditor(editable = true) {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': editable ? 3 : 1 })
  vi.mocked(apiFetch).mockImplementation(async (_, init) => init?.method === 'PUT'
    ? new Response(null, { status: 204 }) : Response.json({ supported: true, editable, profile: 'MTR 1.9 packed',
      length: 2801, revision: 4, sha256: 'fixture-sha', sections: sections() }))
  render(<MemoryRouter><NativeTableEditor setId="fixture" number={27} revision={4} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} /></MemoryRouter>)
}

it('stages multi-row edits and imports in one revision while preserving unknown cells and supports undo', async () => {
  renderEditor()
  fireEvent.click(await screen.findByRole('button', { name: 'Visual prompt worksheet' }))
  expect(screen.getByText(/140 wire rows · 0 staged · 1 unmapped/)).toBeVisible()
  fireEvent.change(screen.getByRole('textbox', { name: /Row 0 text/ }), { target: { value: 'Café ouvert' } })
  fireEvent.click(screen.getByRole('button', { name: 'Replace row 1' }))
  fireEvent.click(screen.getByRole('button', { name: 'Undo row 1' }))
  expect(screen.getByText('Unmapped glyphs, original bytes retained')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Next rows' }))
  expect(screen.getByRole('textbox', { name: /Row 25 text/ })).toBeVisible()
  fireEvent.change(screen.getByRole('textbox', { name: /Row 25 text/ }), { target: { value: 'Deuxième' } })
  screen.getByText('Import a worksheet or review supported glyphs').closest('details')!.open = true
  fireEvent.change(screen.getByLabelText('Worksheet JSON'), { target: { value: document([{ row: 1, text: null }, { row: 139, text: 'Fin' }]) } })
  fireEvent.click(screen.getByRole('button', { name: 'Review worksheet import' }))
  expect(screen.getByText('MT28 to MT27: 1 text changes, 139 retained rows.')).toBeVisible()
  expect(screen.getByText(/140 wire rows · 2 staged/)).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Stage reviewed rows' }))
  expect(screen.getByText(/140 wire rows · 3 staged · 1 unmapped/)).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(vi.mocked(apiFetch).mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(true))
  const body = JSON.parse(vi.mocked(apiFetch).mock.calls.find(([, init]) => init?.method === 'PUT')![1]!.body as string)
  expect(body).toMatchObject({ revision: 4, sha256: 'fixture-sha', changes: { 'prompt-0.text': 'Café ouvert', 'prompt-25.text': 'Deuxième', 'prompt-139.text': 'Fin' } })
  expect(Object.keys(body.changes)).toHaveLength(3)
})

it('blocks an invalid staged row even when it is not on the current page', async () => {
  renderEditor()
  fireEvent.click(await screen.findByRole('button', { name: 'Visual prompt worksheet' }))
  fireEvent.change(screen.getByRole('textbox', { name: /Row 0 text/ }), { target: { value: 'a'.repeat(21) } })
  fireEvent.click(screen.getByRole('button', { name: 'Next rows' }))
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  expect(screen.getByText('Wire row 0 exceeds 20 cells or contains an unsupported glyph.')).toBeVisible()
  expect(vi.mocked(apiFetch).mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(false)
})

it('offers a read-only worksheet and never exposes import or replacement controls', async () => {
  renderEditor(false)
  fireEvent.click(await screen.findByRole('button', { name: 'Visual prompt worksheet' }))
  expect(screen.getByRole('textbox', { name: /Row 0 text/ })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Export worksheet JSON' })).toBeEnabled()
  expect(screen.queryByRole('button', { name: 'Replace row 1' })).toBeNull()
  expect(screen.queryByText('Import a worksheet or review supported glyphs')).toBeNull()
  expect(screen.queryByRole('button', { name: 'Save record' })).toBeNull()
})

it('compares compatible saved companion rows, distinguishes opaque cells, and updates coverage for staged text without editing companions', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  const companionRows = Array.from({ length: 400 }, (_, index) => index === 2 ? 'populated' : index === 3 ? 'unmapped' : 'blank')
  vi.mocked(apiFetch).mockImplementation(async (_, init) => init?.method === 'PUT' ? new Response(null, { status: 204 })
    : Response.json({ supported: true, editable: true, profile: 'Later packed profile', length: 8001, revision: 4,
      sha256: 'fixture-sha', sections: sections(400), visualPromptCompanions: [
        { number: 86, status: 'compatible', rows: companionRows },
        { number: 98, status: 'missing', rows: [] },
        { number: 99, status: 'ambiguous', rows: [] },
      ] }))
  render(<MemoryRouter><NativeTableEditor setId="fixture" number={85} revision={4} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} /></MemoryRouter>)
  fireEvent.click(await screen.findByRole('button', { name: 'Visual prompt worksheet' }))
  expect(screen.getByText('2 rows have both blank and populated language entries.')).toBeVisible()
  expect(screen.getByText('Not in this set', { exact: false })).toBeVisible()
  expect(screen.getByText('Multiple records', { exact: false })).toBeVisible()
  expect(screen.getByText('MT86: Unmapped glyphs')).toBeVisible()
  fireEvent.change(screen.getByLabelText('Show rows'), { target: { value: 'coverage' } })
  expect(screen.getByRole('textbox', { name: /Row 0 text/ })).toBeVisible()
  expect(screen.getByRole('textbox', { name: /Row 2 text/ })).toBeVisible()
  expect(screen.queryByText('Row 1', { exact: true })).toBeNull()
  expect(screen.queryByText('Row 3', { exact: true })).toBeNull()
  fireEvent.change(screen.getByRole('textbox', { name: /Row 2 text/ }), { target: { value: 'Translated' } })
  expect(screen.getByText('1 row has both blank and populated language entries.')).toBeVisible()
  expect(screen.queryByRole('textbox', { name: /Row 2 text/ })).toBeNull()
  fireEvent.change(screen.getByLabelText('Show rows'), { target: { value: 'changed' } })
  expect(screen.getByRole('textbox', { name: /Row 2 text/ })).toHaveValue('Translated')
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(vi.mocked(apiFetch).mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(true))
  const [path, options] = vi.mocked(apiFetch).mock.calls.find(([, init]) => init?.method === 'PUT')!
  expect(path).toBe('/api/tables/sets/fixture/records/85/fields')
  expect(JSON.parse(options!.body as string).changes).toEqual({ 'prompt-2.text': 'Translated' })
})
