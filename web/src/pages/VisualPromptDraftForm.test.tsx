import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'
import { VisualPromptDraftForm } from './VisualPromptDraftForm'
import { promptWorksheetFormat } from './visualPromptWorksheetCodec'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

const spec = (number = 27, count = 140) => ({ number, profile: count === 140 ? 'MTR 1.9 packed' : 'Later packed profile', length: 1 + count * 20,
  rows: Array.from({ length: count }, (_, row) => ({ row, key: `prompt-${row}.text`, label: `Message row ${row}` })) })
const missing = { supported: false, reason: 'Author a new fixed-row visual prompt worksheet, or import a complete record.', promptAuthoring: spec(),
  visualPromptCompanions: [{ number: 28, status: 'compatible', rows: Array.from({ length: 140 }, (_, row) => row === 0 || row === 139 ? 'populated' : 'blank') }] }
const worksheet = (rows: unknown[]) => JSON.stringify({ format: promptWorksheetFormat, recordNumber: 28, rowCount: 140, rows })

it('authors missing prompts through reviewed worksheet import and preserves staged data on a failed create', async () => {
  consoleSession.save('fixture-token', '', 'Fixture administrator', 'Admin')
  let created = false
  const saved = vi.fn(), dirty = vi.fn()
  vi.mocked(apiFetch).mockImplementation(async () => Response.json(created ? {
    supported: true, editable: true, profile: spec().profile, length: 2801, revision: 1, sha256: 'fixture-hash',
    sections: spec().rows.map(({ row, key, label }) => ({ key: `prompt-${row}`, label, fields: [{ key, label: 'Display text',
      editor: 'text', displayGlyphs: true, maxLength: 20, value: row === 0 ? 'Café' : row === 139 ? 'Fin' : '', min: 0, max: 0, choices: null, help: null }] })),
  } : missing))
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ error: 'Fixture conflict' }, { status: 409 }))
    .mockImplementationOnce(async () => { created = true; return new Response(null, { status: 204 }) })
  render(<NativeTableEditor setId="fixture" number={27} revision={0} exchangeFile={null} onExchangeFile={vi.fn()} onSaved={saved} onDirty={dirty} />)
  expect(await screen.findByRole('button', { name: 'Create blank prompt draft' })).toBeEnabled()
  expect(screen.getByText('2 rows have both blank and populated language entries.')).toBeVisible()
  fireEvent.change(screen.getByRole('textbox', { name: /Row 0 text/ }), { target: { value: '漢' } })
  expect(screen.getByRole('button', { name: 'Create visual prompt record' })).toBeDisabled()
  fireEvent.change(screen.getByRole('textbox', { name: /Row 0 text/ }), { target: { value: 'Café' } })
  expect(dirty).toHaveBeenLastCalledWith(true)
  screen.getByText('Import a worksheet or review supported glyphs').closest('details')!.open = true
  fireEvent.change(screen.getByLabelText('Worksheet JSON'), { target: { value: worksheet([{ row: 1, text: null }, { row: 139, text: 'Fin' }]) } })
  fireEvent.click(screen.getByRole('button', { name: 'Review worksheet import' }))
  expect(screen.getByText('MT28 to MT27: 1 text changes, 139 retained rows.')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Stage reviewed rows' }))
  expect(screen.getByText(/2 populated rows · 138 blank rows/)).toBeVisible()
  expect(screen.getByText('0 rows have both blank and populated language entries.')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Create visual prompt record' }))
  expect(await screen.findByText('Fixture conflict')).toBeVisible()
  expect(screen.getByRole('textbox', { name: /Row 0 text/ })).toHaveValue('Café')
  expect(screen.getByText(/2 populated rows · 138 blank rows/)).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Create visual prompt record' }))
  await waitFor(() => expect(saved).toHaveBeenCalledTimes(1))
  expect(apiPostRaw).toHaveBeenLastCalledWith('/api/tables/sets/fixture/records/27/fields/prompt-authoring', {
    profile: spec().profile, length: 2801, rows: { 0: 'Café', 139: 'Fin' },
  })
  expect(dirty).toHaveBeenLastCalledWith(false)
  await waitFor(() => expect(screen.queryByRole('form', { name: 'Create visual prompt record' })).toBeNull())
  expect(screen.getByRole('textbox', { name: /Row 0 text/ })).toHaveValue('Café')
}, 15000)

it('offers an explicitly blank 400-row draft without inserting assumed language text', () => {
  const create = vi.fn()
  render(<VisualPromptDraftForm spec={spec(85, 400)} busy={false} onCreate={create} onDirty={vi.fn()} />)
  expect(screen.getByText(/0 populated rows · 400 blank rows/)).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Create blank prompt draft' }))
  expect(create).toHaveBeenCalledWith({ profile: 'Later packed profile', length: 8001, rows: {} })
})

it('does not offer a missing-record authoring worksheet to a reader', async () => {
  consoleSession.save('fixture-token', '', 'Fixture reader', 'Operator', { 'table-records': 1 })
  vi.mocked(apiFetch).mockResolvedValue(Response.json(missing))
  render(<NativeTableEditor setId="fixture" number={27} revision={0} exchangeFile={null} onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} />)
  expect(await screen.findByText(missing.reason)).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Create blank prompt draft' })).toBeNull()
})
