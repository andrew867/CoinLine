import { cleanup, render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { UploadDetail } from './UploadDetail'
import type { MessagingReference } from './MessagingReportReference'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostBareOk: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

const source: MessagingReference = {
  status: 'acknowledged', downloadBatchId: 'fixture-messaging-download', tableRevision: 2,
  ackRecordedAtUtc: '2028-04-01T11:40:00Z', laterUncertainAttempt: true,
  settings: { serviceMode: 1, callRecordMode: 0, collectStatistics: true,
    allowedPayments: ['Coins'], areas: [
      { area: 'Local', allowed: true, surchargeCents: 25 },
      { area: 'Intra-area toll', allowed: false, surchargeCents: 256 },
      { area: 'Inter-area toll', allowed: false, surchargeCents: 0 },
      { area: 'International', allowed: false, surchargeCents: 65535 },
    ] },
}

function show(reference: MessagingReference | null, type = 69) {
  vi.mocked(apiFetch).mockResolvedValue(Response.json({
    id: 'fixture-upload', terminalId: 'fixture-phone', status: 2,
    idempotencyKey: 'fixture', rawPayloadHex: '', decodedMetadataJson: '{}',
    isTerminalStatistics: true, relatedDlogTransactionId: null,
    createdAtUtc: '2028-04-01T12:00:00Z', updatedAtUtc: '2028-04-01T12:00:00Z',
    messagingReference: reference,
    records: [{ id: 'fixture-record', terminalStatistics: {
      messageType: type, name: 'Fixture statistics', periodStart: null, periodEnd: null,
      groups: [{ label: 'Ring no answer', counters: [{ label: 'Local', value: 17 }] }],
      carriers: [], carrierFormat: null,
    } }],
  }))
  render(<MemoryRouter initialEntries={['/uploads/fixture-upload']}><Routes>
    <Route path="/uploads/:id" element={<UploadDetail />} />
  </Routes></MemoryRouter>)
}

it('shows retained MT68 settings and configured surcharges separately from received MT69 counts', async () => {
  show(source)
  const panel = await screen.findByRole('region', { name: 'MT68 Audio-Gram configuration reference' })
  expect(panel).toHaveTextContent('Revision 2')
  expect(panel).toHaveTextContent(/later attempt has no confirmed/)
  expect(panel).toHaveTextContent(/not the current assignment/)
  expect(panel).toHaveTextContent(/do not establish collected or settled amounts/)
  expect(within(panel).getByRole('link', { name: 'View source download' })).toHaveAttribute('href', '/downloads/fixture-messaging-download')
  expect(panel).toHaveTextContent('Coins')
  const table = within(panel).getByRole('table', { name: 'Configured Audio-Gram call areas and surcharges' })
  expect(within(table).getByRole('row', { name: 'Local Allowed 25 cents' })).toBeVisible()
  expect(within(table).getByRole('row', { name: 'Intra-area toll Not selected 256 cents' })).toBeVisible()
  expect(within(table).getByRole('row', { name: /International Not selected/ })).toHaveTextContent('65,535 cents')
  expect(screen.getByText('17')).toBeVisible()
})

it.each(['missing', 'ambiguous', 'restricted'] as const)('retains report counts without exposing %s reference data', async status => {
  show({ ...source, status })
  const panel = await screen.findByRole('region', { name: 'MT68 Audio-Gram configuration reference' })
  expect(within(panel).queryByRole('link')).toBeNull()
  expect(within(panel).queryByRole('table')).toBeNull()
  expect(panel).not.toHaveTextContent('Coins')
  expect(screen.getByText('17')).toBeVisible()
})

it('distinguishes an invalid acknowledged record from an unknown or absent policy', async () => {
  show({ ...source, status: 'invalid', settings: null })
  const panel = await screen.findByRole('region', { name: 'MT68 Audio-Gram configuration reference' })
  expect(panel).toHaveTextContent('does not match the supported 72-byte MT68 layout')
  expect(within(panel).getByRole('link')).toBeVisible()
  expect(within(panel).queryByRole('table')).toBeNull()
})

it('does not convert noncanonical mode codes into an enabled configuration', async () => {
  show({ ...source, settings: { ...source.settings!, serviceMode: 3, callRecordMode: 2 } })
  const panel = await screen.findByRole('region', { name: 'MT68 Audio-Gram configuration reference' })
  expect(panel).toHaveTextContent('Unrecognized stored code 3')
  expect(panel).toHaveTextContent('Unrecognized stored code 2')
})

it('does not attach a messaging reference to another statistics type', async () => {
  show(source, 56)
  await screen.findByRole('heading', { name: 'Fixture statistics' })
  expect(screen.queryByRole('region', { name: 'MT68 Audio-Gram configuration reference' })).toBeNull()
})

it('distinguishes an unavailable reference response from a confirmed missing acknowledgement', async () => {
  show(null)
  const panel = await screen.findByRole('region', { name: 'MT68 Audio-Gram configuration reference' })
  expect(panel).toHaveTextContent('No MT68 configuration reference was returned')
  expect(panel).not.toHaveTextContent('No retained MT68 application acknowledgement was found')
  expect(screen.getByText('17')).toBeVisible()
})
