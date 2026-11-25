import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { TableSetConfiguration } from './TableSetConfiguration'
import type { AdvertisingSlot } from './advertisingPlayback'
import { advertisingLine } from './advertisingPlayback'
import { authorableAdvertisingText, normalizedAdvertisingText, westernDisplayGlyphs } from './advertisingGlyphs'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

const endpoint = '/api/tables/sets/set-fixture/advertising'
const blank = (): AdvertisingSlot => ({ text: '', durationTicks: 0, mode: 0, blankBefore: false, endSequence: true,
  spareByte: 0, reservedFlags: 0, containsUnmappedGlyphs: false })
function fixture() {
  const slots = Array.from({ length: 20 }, blank)
  slots[0] = { ...slots[0], text: 'A\uFFFD B', endSequence: false, durationTicks: 125, mode: 3, blankBefore: true,
    spareByte: 165, reservedFlags: 144, containsUnmappedGlyphs: true }
  slots[1] = { ...slots[1], text: 'Caf', durationTicks: 500, endSequence: false }
  return { slots, revision: 1, sha256: 'fixture-digest', onHookEnabled: true, quickAccessEnabled: false, callEstablishedEnabled: false, scrollTicks: 10 }
}

async function openEditor() {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  let stored = fixture()
  vi.mocked(apiFetch).mockImplementation(async (url, init) => {
    if (url !== endpoint) return new Response(null, { status: 404 })
    if (init?.method === 'PUT') {
      const body = JSON.parse(init.body as string)
      stored = { ...stored, slots: body.slots, revision: stored.revision + 1 }
      return new Response(null, { status: 204 })
    }
    return Response.json(stored)
  })
  render(<TableSetConfiguration setId="set-fixture" draft initialRecord={29} recordDirty={false}
    versions={[{ tableDefinitionId: 'fixture-definition', tableNumber: 29, definitionName: 'Text advertising',
      tableRevision: 1, sortOrder: 1, dependsOnTableDefinitionId: null }]} onSaved={vi.fn()} onRecordDirty={vi.fn()} />)
  const summary = screen.getByText('Edit advertising messages')
  const details = summary.closest('details')!
  details.open = true
  fireEvent(details, new Event('toggle'))
  await screen.findByRole('group', { name: 'Message 1' })
}
const message = (number: number) => within(screen.getByRole('group', { name: `Message ${number}` }))
function savedSlots(): AdvertisingSlot[] {
  const put = vi.mocked(apiFetch).mock.calls.find(([url, init]) => url === endpoint && init?.method === 'PUT')
  return JSON.parse(put![1]!.body as string).slots
}

it('supports Western characters and a 20-cell NFC preview', () => {
  expect(authorableAdvertisingText(westernDisplayGlyphs.slice(0, 20))).toBe(true)
  expect(authorableAdvertisingText('a\u0300'.repeat(20))).toBe(true)
  expect(authorableAdvertisingText('é'.repeat(21))).toBe(false)
  expect(authorableAdvertisingText('漢')).toBe(false)
  expect(authorableAdvertisingText('\uFFFD')).toBe(false)
  expect(normalizedAdvertisingText('Cafe\u0301')).toBe('Café')
  expect(advertisingLine('Cafe\u0301')).toBe('Café'.padEnd(20))
})

it('inserts a French glyph, previews it and saves explicit spare edits without replacing imported text', async () => {
  await openEditor()
  expect(message(1).getByText(/Imported text contains unmapped cells/)).toBeVisible()
  expect(message(1).getByLabelText('Text')).toHaveValue('A\uFFFD B')
  fireEvent.change(message(1).getByLabelText('Seconds'), { target: { value: '2' } })
  const spareDetails = message(1).getByText('Spare fields').closest('details')!
  spareDetails.open = true
  expect(message(1).getByLabelText('Spare attribute bit 4 (0x10)')).toBeChecked()
  expect(message(1).getByLabelText('Spare attribute bit 7 (0x80)')).toBeChecked()
  expect(message(1).getByLabelText('Spare byte')).toHaveValue(165)
  fireEvent.click(message(1).getByLabelText('Spare attribute bit 4 (0x10)'))
  fireEvent.click(message(1).getByLabelText('Spare attribute bit 7 (0x80)'))
  fireEvent.click(message(1).getByLabelText('Spare attribute bit 5 (0x20)'))
  fireEvent.change(message(1).getByLabelText('Spare byte'), { target: { value: '87' } })

  screen.getByText('Display character palette').closest('details')!.open = true
  fireEvent.change(screen.getByLabelText('Language palette'), { target: { value: 'french' } })
  const input = message(2).getByLabelText('Text') as HTMLInputElement
  fireEvent.focus(input)
  input.setSelectionRange(3, 3)
  fireEvent.click(screen.getByRole('button', { name: 'Insert é into message 2' }))
  expect(input).toHaveValue('Café')
  expect(screen.getByRole('img', { name: 'Terminal display, lower line Café' })).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Save advertising' }))
  await screen.findByText('Advertising saved to this draft.')
  const slots = savedSlots()
  expect(slots[0]).toMatchObject({ text: 'A\uFFFD B', durationTicks: 200, mode: 3, blankBefore: true,
    spareByte: 87, reservedFlags: 32, containsUnmappedGlyphs: true })
  expect(slots[1]).toMatchObject({ text: 'Café', durationTicks: 500, spareByte: 0, reservedFlags: 0, endSequence: false })
})

it('validates block changes, permits unchanged unmapped text and does not clobber slot spares', async () => {
  await openEditor()
  fireEvent.change(message(1).getByLabelText('Text'), { target: { value: 'A\uFFFD B changed' } })
  expect(screen.getByRole('button', { name: 'Save advertising' })).toBeDisabled()
  fireEvent.change(message(1).getByLabelText('Text'), { target: { value: 'A\uFFFD B' } })
  expect(screen.getByRole('button', { name: 'Save advertising' })).toBeEnabled()
  fireEvent.click(screen.getByRole('button', { name: 'Edit as text block' }))
  const block = screen.getByLabelText('Advertising text block')
  fireEvent.change(block, { target: { value: 'A\uFFFD B\n漢' } })
  fireEvent.click(screen.getByRole('button', { name: 'Apply block text' }))
  expect(screen.getByText(/Use up to 20 lines of 20 display cells/)).toBeVisible()
  fireEvent.change(block, { target: { value: 'A\uFFFD B\nman\u0303ana' } })
  fireEvent.click(screen.getByRole('button', { name: 'Apply block text' }))
  expect(message(2).getByLabelText('Text')).toHaveValue('mañana')
  fireEvent.click(screen.getByRole('button', { name: 'Save advertising' }))
  await waitFor(() => expect(vi.mocked(apiFetch).mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(true))
  expect(savedSlots()[0]).toMatchObject({ text: 'A\uFFFD B', spareByte: 165, reservedFlags: 144, containsUnmappedGlyphs: true })
  expect(savedSlots()[1]).toMatchObject({ text: 'mañana', durationTicks: 500 })
})
