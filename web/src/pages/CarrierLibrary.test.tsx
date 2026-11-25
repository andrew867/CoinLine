import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiGet } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { CarrierLibraryPage, type CarrierEntry } from './CarrierLibraryPage'
import { CarrierLibraryPanel } from './CarrierLibraryPanel'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

const entry: CarrierEntry = { id: 'carrier-fixture', code: 'CARR', description: 'Fixture carrier', tableNumber: 23,
  revision: 3, reference: '9', displayName: 'Fixture Carrier', featureGroupB: true, screeningEntry: '8' }

it('requires an explicit target screening position and card acceptance choice before applying a reusable row', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'carrier-library': 1, 'table-records': 3 })
  vi.mocked(apiGet).mockResolvedValue([entry])
  vi.mocked(apiFetch).mockResolvedValue(new Response(null, { status: 204 }))
  const applied = vi.fn()
  render(<MemoryRouter><CarrierLibraryPanel setId="set-fixture" number={23} slot={5} revision={4} sha256="fixture-hash"
    reference="18" editable dirty={false} onApplied={applied} /></MemoryRouter>)
  const choice = await screen.findByRole('option', { name: 'CARR · Fixture Carrier' })
  expect(choice).toBeVisible()
  fireEvent.change(screen.getByLabelText('Library carrier'), { target: { value: entry.id } })
  expect(screen.getByRole('button', { name: 'Apply carrier definition' })).toBeDisabled()
  expect(screen.getByLabelText('Card acceptance for this row')).toHaveValue('clear')
  expect(screen.getByLabelText('Reserved fields for this row')).toHaveValue('keep')
  fireEvent.change(screen.getByLabelText(/Target call-screening entry/), { target: { value: '12' } })
  fireEvent.change(screen.getByLabelText('Card acceptance for this row'), { target: { value: 'keep' } })
  fireEvent.click(screen.getByRole('button', { name: 'Apply carrier definition' }))
  await waitFor(() => expect(applied).toHaveBeenCalledOnce())
  const request = vi.mocked(apiFetch).mock.calls[0]
  expect(request[0]).toBe('/api/carrier-library/carrier-fixture/apply')
  expect(JSON.parse(request[1]?.body as string)).toEqual({
    setId: 'set-fixture', slot: 5, libraryRevision: 3, recordRevision: 4, recordSha256: 'fixture-hash',
    keepCardAcceptance: true, screeningEntry: 12, copySpareFields: false,
  })
  fireEvent.change(screen.getByLabelText('Reserved fields for this row'), { target: { value: 'copy' } })
  fireEvent.click(screen.getByRole('button', { name: 'Apply carrier definition' }))
  await waitFor(() => expect(applied).toHaveBeenCalledTimes(2))
  expect(JSON.parse(vi.mocked(apiFetch).mock.calls[1][1]?.body as string)).toMatchObject({
    keepCardAcceptance: true, screeningEntry: 12, copySpareFields: true,
  })
  expect(screen.getByLabelText('Reserved fields for this row')).toHaveValue('keep')
})

it('permits Add-only creation without listing the carrier catalogue and previews the display name', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'carrier-library': 4 })
  const fields = [
    { key: 'carrier-0.reference', label: 'Carrier reference', value: '255', editor: 'number', min: 0, max: 255, maxLength: null, choices: null, help: null },
    { key: 'carrier-0.name', label: 'Display name', value: '', editor: 'text', min: 0, max: 0, maxLength: 20, choices: null, help: null },
    { key: 'carrier-0.spare.byte1', label: 'Carrier row spare byte', value: '0', editor: 'number', min: 0, max: 255,
      maxLength: null, choices: null, help: null, isSpare: true },
  ]
  vi.mocked(apiFetch).mockImplementation(async (_, options) => options?.method === 'POST'
    ? Response.json({ entry, fields }, { status: 201 }) : Response.json({ fields }))
  vi.mocked(apiGet).mockResolvedValue({ fields })
  render(<MemoryRouter><CarrierLibraryPage /></MemoryRouter>)
  const ref = await screen.findByLabelText('Carrier reference')
  fireEvent.change(ref, { target: { value: '9' } })
  fireEvent.change(screen.getByLabelText('Carrier ID'), { target: { value: 'CARR' } })
  fireEvent.change(screen.getByLabelText('Display name'), { target: { value: 'Fixture Carrier' } })
  const spareDetails = screen.getByText('Spare fields').closest('details')!
  expect(spareDetails.open).toBe(false)
  spareDetails.open = true
  fireEvent.change(screen.getByLabelText('Carrier row spare byte'), { target: { value: '197' } })
  expect(screen.getByRole('img', { name: /lower line Fixture Carrier/ })).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Save carrier' }))
  await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('Carrier definition saved.'))
  expect(vi.mocked(apiGet).mock.calls.some(call => call[0] === '/api/carrier-library')).toBe(false)
  const posted = vi.mocked(apiFetch).mock.calls.find(call => call[1]?.method === 'POST')
  expect(JSON.parse(posted?.[1]?.body as string)).toMatchObject({ code: 'CARR', tableNumber: 23,
    changes: { 'carrier-0.reference': '9', 'carrier-0.name': 'Fixture Carrier', 'carrier-0.spare.byte1': '197' } })
})
