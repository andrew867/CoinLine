import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

const layout = (familyId: string, editable = true, slots = 60) => ({
  supported: true, editable, profile: 'Compact screening, 12 bytes per entry', length: 1 + slots * 12,
  revision: 2, sha256: 'fixture-hash',
  screening: { descriptions: { 50: 'Institutional reference', 60: 'Extra slot reference' }, supportsSpeedDial: false,
    family: { familyId, familyLabel: familyId === 'msr17-inmate' ? 'MSR 1.7 Inmate' : 'MTR 1.9 Multi-pay',
      documentedEntries: familyId === 'msr17-inmate' ? 50 : 200,
      institutionalFirst: familyId === 'msr17-inmate' ? 16 : null, institutionalLast: familyId === 'msr17-inmate' ? 50 : null },
    savedRetainedSlots: [1, 16, 50, 51, 60] },
  sections: Array.from({ length: slots }, (_, index) => ({ key: `rule-${index}`, label: `Screening rule ${index + 1}`,
    fields: [{ key: `rule-${index}.answer`, label: 'Require answer supervision', editor: 'checkbox', value: 'false',
      min: 0, max: 1, maxLength: null, choices: null, help: null }] })),
})

function fixture(familyId: string, onSaved = vi.fn()) {
  return <MemoryRouter><NativeTableEditor setId="fixture" number={24} revision={2} familyId={familyId}
    exchangeFile={null} onExchangeFile={vi.fn()} onSaved={onSaved} onDirty={vi.fn()} /></MemoryRouter>
}

it('separates operator, institutional and extra packed slots without discarding staged edits', async () => {
  consoleSession.save('fixture-token', '', 'Fixture administrator', 'Admin')
  const onSaved = vi.fn()
  vi.mocked(apiFetch).mockImplementation(async (url, init) => init?.method === 'PUT'
    ? new Response(null, { status: 204 })
    : Response.json(layout(new URL(String(url), 'http://localhost').searchParams.get('familyId')!)))
  render(fixture('msr17-inmate', onSaved))
  const panel = await screen.findByRole('region', { name: 'Screening family workspace' })
  expect(within(panel).getByText(/50 entries; this record has 60 packed slots/)).toBeVisible()
  expect(within(panel).getByText(/Institutional slots with retained settings or descriptions: 16, 50/)).toBeVisible()
  expect(within(panel).getByText(/Slots beyond the documented list with retained settings or descriptions: 51, 60/)).toBeVisible()
  const entry = screen.getByLabelText('Section or entry') as HTMLSelectElement
  expect(entry.options).toHaveLength(15)
  expect(Array.from(entry.options).some(option => option.value === 'rule-15')).toBe(false)
  fireEvent.click(screen.getByLabelText('Require answer supervision'))
  fireEvent.change(screen.getByLabelText('Screening view'), { target: { value: 'institution' } })
  expect(entry.options).toHaveLength(35)
  expect(entry).toHaveValue('rule-15')
  expect(screen.getByText(/Entry 16 is in the documented institutional range/)).toBeVisible()
  fireEvent.change(screen.getByLabelText('Screening view'), { target: { value: 'wire' } })
  expect(entry.options).toHaveLength(60)
  fireEvent.change(entry, { target: { value: 'rule-59' } })
  expect(screen.getByText(/Entry 60 is outside the documented list/)).toBeVisible()
  expect(screen.getByLabelText('Description')).toHaveValue('Extra slot reference')
  fireEvent.change(screen.getByLabelText('Screening view'), { target: { value: 'legacy' } })
  fireEvent.change(entry, { target: { value: 'rule-0' } })
  expect(screen.getByLabelText('Require answer supervision')).toBeChecked()
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(onSaved).toHaveBeenCalled())
  const save = vi.mocked(apiFetch).mock.calls.find(([, init]) => init?.method === 'PUT')!
  expect(save[0]).toBe('/api/tables/sets/fixture/records/24/fields')
  expect(JSON.parse(String(save[1]?.body))).toMatchObject({ revision: 2, sha256: 'fixture-hash', changes: { 'rule-0.answer': 'true' } })
  expect(JSON.parse(String(save[1]?.body))).not.toHaveProperty('familyId')
}, 15000)

it('uses the selected family context without hiding a shorter packed list or enabling published edits', async () => {
  consoleSession.save('fixture-token', '', 'Fixture administrator', 'Admin')
  vi.mocked(apiFetch).mockImplementation(async url => Response.json(layout(new URL(String(url), 'http://localhost').searchParams.get('familyId')!, false)))
  const { rerender } = render(fixture('msr17-inmate'))
  expect(await screen.findByText('MSR 1.7 Inmate screening')).toBeVisible()
  rerender(fixture('mtr19-multipay'))
  expect(await screen.findByText('MTR 1.9 Multi-pay screening')).toBeVisible()
  expect(screen.getByText(/The packed record is shorter than the documented list/)).toBeVisible()
  expect(screen.queryByLabelText('Screening view')).toBeNull()
  expect((screen.getByLabelText('Section or entry') as HTMLSelectElement).options).toHaveLength(60)
  expect(screen.getByLabelText('Require answer supervision')).toBeDisabled()
  expect(screen.queryByRole('button', { name: 'Save record' })).toBeNull()
})
