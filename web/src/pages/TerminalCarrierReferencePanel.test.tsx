import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'
import { TerminalCarrierReferencePanel } from './TerminalCarrierReferencePanel'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

const response = { reference: 17, setRevision: 2, profile: 'Later fixture', review: { supported: true, reason: null,
  matchingRows: [{ row: 2, acceptedCardRanges: [1, 20] }, { row: 5, acceptedCardRanges: [] }],
  references: [{ recordNumber: 135, label: 'Default coin carrier', use: 'retained' },
    { recordNumber: 134, label: 'Card range 1, selected carrier', use: 'configured' },
    { recordNumber: 92, label: 'Screening rule 1, explicit free Feature Group B carrier', use: 'conditional' }],
  reviewedRecords: [92, 134, 135], missingRecords: [55], unreviewedRecords: [26], warnings: ['Multiple carrier rows use this identifier.'] } }

it('reviews the original saved identifier, not its row position or staged replacement', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  const layout = { supported: true, editable: true, profile: 'Later fixture', length: 1109, revision: 1, sha256: 'fixture-hash',
    sections: [2, 3].map((row, index) => ({ key: `carrier-${row}`, label: `Carrier ${row}`, fields: [
      { key: `carrier-${row}.reference`, label: 'Carrier reference', editor: 'number', value: index === 0 ? '17' : '21', min: 0, max: 255, choices: null, help: null },
    ] })) }
  vi.mocked(apiFetch).mockImplementation(async path => {
    const selected = String(path).match(/carriers\/(\d+)\/references$/)
    if (!selected) return Response.json(layout)
    if (selected[1] === '17') return Response.json(response)
    return Response.json({ ...response, reference: 21, review: { ...response.review, matchingRows: [{ row: 3, acceptedCardRanges: [] }], references: [], warnings: [] } })
  })
  render(<NativeTableEditor setId="fixture" number={135} revision={1} exchangeFile={null} onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} />)
  expect(await screen.findByRole('heading', { name: 'What uses carrier reference 17?' })).toBeVisible()
  fireEvent.change(screen.getByLabelText('Carrier reference'), { target: { value: '18' } })
  expect(screen.getByRole('note')).toHaveTextContent('original saved carrier identifier')
  fireEvent.click(screen.getByRole('button', { name: 'Review carrier references' }))
  expect(await screen.findByText('Carrier row 2: Acceptance bits for card ranges 1, 20.')).toBeVisible()
  expect(screen.getByText('Carrier row 5: No card acceptance bits set.')).toBeVisible()
  expect(screen.getByText(/Default coin carrier.*Retained setting/)).toBeVisible()
  expect(screen.getByText(/Card range 1.*Configured reference/)).toBeVisible()
  expect(screen.getByText(/Screening rule 1.*Conditional reference/)).toBeVisible()
  expect(screen.getByText(/Not present here: MT55/)).toBeVisible()
  expect(screen.getByText(/duplicate records not reviewed: MT26/)).toBeVisible()
  expect(screen.getByText('Multiple carrier rows use this identifier.')).toBeVisible()
  expect(apiFetch).toHaveBeenCalledWith('/api/tables/sets/fixture/carriers/17/references', expect.objectContaining({ signal: expect.any(AbortSignal) }))
  fireEvent.change(screen.getByLabelText('Section or entry'), { target: { value: 'carrier-3' } })
  expect(screen.getByRole('heading', { name: 'What uses carrier reference 21?' })).toBeVisible()
  expect(screen.queryByText('Multiple carrier rows use this identifier.')).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Review carrier references' }))
  expect(await screen.findByText(/This does not establish that the carrier is unused/)).toBeVisible()
  expect(vi.mocked(apiFetch).mock.calls.every(([, options]) => !options?.method || options.method === 'GET')).toBe(true)
})

it('does not treat a request failure or unsupported layout as an empty successful review', async () => {
  vi.mocked(apiFetch).mockResolvedValueOnce(new Response(null, { status: 500 }))
    .mockResolvedValueOnce(Response.json({ ...response, review: { ...response.review, supported: false, reason: 'Choose a verified carrier layout.' } }))
  render(<TerminalCarrierReferencePanel setId="fixture" reference={17} />)
  fireEvent.click(screen.getByRole('button', { name: 'Review carrier references' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('HTTP 500')
  fireEvent.click(screen.getByRole('button', { name: 'Review carrier references' }))
  expect(await screen.findByRole('status')).toHaveTextContent('Choose a verified carrier layout.')
  expect(screen.queryByText(/No recognized references/)).toBeNull()
})
