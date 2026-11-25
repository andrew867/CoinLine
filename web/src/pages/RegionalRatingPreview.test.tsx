import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'
import { previewInternational, previewNpa } from './regionalRatingLookup'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

const internationalValues = (overrides: Record<string, string | null> = {}) =>
  (key: string) => key in overrides ? overrides[key] : /^country-\d+\.(country|rate)$/.test(key) ? '0' : undefined

it('selects the correct half of the NPA pair, including boundary and special codes', () => {
  const fields: Record<string, string> = {
    'pair-0.even': '2', 'pair-0.odd': '15', 'pair-399.even': '1', 'pair-399.odd': '0',
  }
  const value = (key: string) => fields[key]
  expect(previewNpa('200', value)).toMatchObject({ action: { kind: 'tariff', row: 16 }, section: 'pair-0' })
  expect(previewNpa('201', value).action).toEqual({ kind: 'tariff', row: 29 })
  expect(previewNpa('998', value).action).toEqual({ kind: 'reject' })
  expect(previewNpa('999', value)).toMatchObject({ action: { kind: 'external' }, note: expect.stringContaining('ten-digit blocking') })
  expect(previewNpa('199', value).action.kind).toBe('unknown')
  expect(previewNpa('200', () => '16').action.kind).toBe('unknown')
  expect(previewNpa('200', () => '').action.kind).toBe('unknown')
})

it('chooses the longest numeric international prefix and the first duplicate in wire order', () => {
  const value = internationalValues({
    'country-0.country': '44', 'country-0.rate': '2',
    'country-4.country': '44123', 'country-4.rate': '99',
    'country-199.country': '44123', 'country-199.rate': '1',
  })
  expect(previewInternational('44123456', value)).toMatchObject({
    action: { kind: 'tariff', row: 127 }, section: 'country-4', duplicateSlots: [200],
  })
  expect(previewInternational('44999', value)).toMatchObject({ action: { kind: 'tariff', row: 30 }, section: 'country-0' })
  expect(previewInternational('44', value).action).toEqual({ kind: 'tariff', row: 30 })
  expect(previewInternational('044123', value).action).toEqual({ kind: 'tariff', row: 30 })
  expect(previewInternational('44#', value).action.kind).toBe('unknown')
  expect(previewInternational('44', internationalValues({ 'country-0.country': null })).action.kind).toBe('unknown')
})

it('distinguishes direct no-match codes from matched high-value fallback codes', () => {
  const fixture = { 'country-0.country': '44', 'country-0.rate': '255', noMatch: '1', fallback: '0' }
  expect(previewInternational('44123', internationalValues(fixture))).toMatchObject({
    action: { kind: 'tariff', row: 28 }, fallback: true, note: expect.stringContaining('reserved tariff row'),
  })
  expect(previewInternational('55', internationalValues(fixture))).toMatchObject({ action: { kind: 'external' }, section: 'default' })
  expect(previewInternational('44123', internationalValues({ ...fixture, fallback: '1' })).action).toEqual({ kind: 'tariff', row: 29 })
  expect(previewInternational('55', internationalValues({ ...fixture, fallback: '1' })).action).toEqual({ kind: 'reject' })
  expect(previewInternational('44123', internationalValues({ ...fixture, fallback: '99' })).action).toEqual({ kind: 'tariff', row: 127 })
  expect(previewInternational('44123', internationalValues({ ...fixture, fallback: '100' })).action.kind).toBe('unknown')
  for (const mode of ['2', '3'])
    expect(previewInternational('44123', internationalValues({ ...fixture, noMatch: mode })).action.kind).toBe('reject')
  expect(previewInternational('44123', internationalValues({ ...fixture, noMatch: '0' })).action.kind).toBe('external')
  expect(previewInternational('44123', internationalValues({ ...fixture, 'country-0.rate': '' })).action.kind).toBe('unknown')
})

const field = (key: string, label: string, value: string, max = 255, tariffIndexOffset?: number) => ({
  key, label, value, editor: 'number', min: 0, max, tariffIndexOffset, maxLength: null, choices: null, help: null,
})

it('opens the selected native entry, previews a staged rate change and sends only the edited field', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  const layout = {
    supported: true, editable: true, profile: 'Later packed profile', length: 604, revision: 1, sha256: 'fixture',
    sections: [{ key: 'default', label: 'International fallback', fields: [
      field('noMatch', 'When no destination matches', '0', 3),
      field('fallback', 'Default rate', '0', 99, 28),
    ] }, ...Array.from({ length: 200 }, (_, index) => ({
      key: `country-${index}`, label: `Destination ${index + 1}`, fields: [
        field(`country-${index}.country`, 'Country or destination prefix', index === 199 ? '44123' : '0', 65535),
        field(`country-${index}.rate`, 'Destination rate', '2', 99, 28),
      ],
    }))],
  }
  vi.mocked(apiFetch).mockImplementation(async (_, options) => options?.method === 'PUT'
    ? new Response(null, { status: 204 }) : Response.json(layout))
  const dirty = vi.fn()
  render(<NativeTableEditor setId="set-fixture" number={151} revision={1} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={dirty} />)
  fireEvent.change(await screen.findByLabelText('Test international destination prefix'), { target: { value: '441234' } })
  expect(screen.getByText('MT73 rate 30', { selector: 'strong' })).toBeVisible()
  expect(screen.getByText(/Destination 200, prefix 44123/)).toBeVisible()
  fireEvent.change(screen.getByLabelText('Find entry'), { target: { value: 'International fallback' } })
  fireEvent.click(screen.getByRole('button', { name: 'Open selected entry' }))
  expect(screen.getByLabelText('Section or entry')).toHaveValue('country-199')
  expect(screen.getByLabelText('Country or destination prefix')).toHaveValue(44123)
  expect(dirty).not.toHaveBeenCalledWith(true)
  fireEvent.change(screen.getByLabelText('Destination rate'), { target: { value: '99' } })
  expect(screen.getByText('MT73 rate 127', { selector: 'strong' })).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(vi.mocked(apiFetch).mock.calls.some(([, init]) => init?.method === 'PUT')).toBe(true))
  const request = vi.mocked(apiFetch).mock.calls.find(([, init]) => init?.method === 'PUT')![1]!
  expect(JSON.parse(request.body as string).changes).toEqual({ 'country-199.rate': '99' })
})
