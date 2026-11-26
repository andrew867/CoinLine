import { cleanup, render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { UploadDetail } from './UploadDetail'
import type { CollectionReference } from './CollectionReportReference'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostBareOk: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

const received = '2028-04-01T12:00:00Z'
const collection: CollectionReference = {
  status: 'acknowledged', recordNumber: 34, downloadBatchId: 'collection-fixture', tableRevision: 2,
  ackRecordedAtUtc: '2028-04-01T11:40:00Z', laterUncertainAttempt: true, callRecordThreshold: null,
  collectionSettings: { startHour: 6, startMinute: 15, days: 1, threshold: 3, policyFlags: 0x26,
    timestamps: [{ hour: 7, minute: 20 }, { hour: 0, minute: 0 }, { hour: 0, minute: 0 }, { hour: 0, minute: 0 }],
    detailHour: 6, detailMinute: 15, detailDays: 1, detailHours: 0, detailThreshold: 1 },
}
const callIn: CollectionReference = { ...collection, recordNumber: 35, downloadBatchId: 'callin-fixture', tableRevision: 3,
  laterUncertainAttempt: false, collectionSettings: null, callRecordThreshold: 2 }

function show(type: number, reference: CollectionReference | null = collection, threshold: CollectionReference | null = callIn) {
  const call = type === 53
  vi.mocked(apiFetch).mockResolvedValue(Response.json({
    id: 'fixture-upload', terminalId: 'fixture-phone', status: 2, idempotencyKey: 'fixture',
    isTerminalStatistics: !call, isTerminalCallDetail: call, rawPayloadHex: '', decodedMetadataJson: '{}',
    createdAtUtc: received, updatedAtUtc: received, collectionReference: reference, callInReference: threshold,
    records: [{ id: 'fixture-record', terminalCall: call ? { messageType: 53, sequence: 1, carrier: 1, dialedDigits: '',
      dialedPrefixOnly: true, callType: 'Local', payment: 'Coins', terminalStart: null, durationSeconds: 10,
      amountCharged: 25, amountCollected: 25 } : null, terminalStatistics: call ? null : {
      messageType: type, name: 'Fixture statistics', periodStart: '2028-04-01T06:15:00', periodEnd: '2028-04-01T07:20:00',
      carrierFormat: null, carriers: [], groups: [{ label: 'Call counters', counters: [{ label: 'Local', value: 17 }] }],
    } }],
  }))
  render(<MemoryRouter initialEntries={['/uploads/fixture-upload']}><Routes>
    <Route path="/uploads/:id" element={<UploadDetail />} />
  </Routes></MemoryRouter>)
}

it.each([56, 57, 71])('shows the receipt-time MT34 reference alongside unchanged MT%s report counts', async type => {
  show(type)
  const panel = await screen.findByRole('region', { name: 'MT34 call and carrier collection reference' })
  expect(within(panel).getByRole('link', { name: 'View source download' })).toHaveAttribute('href', '/downloads/collection-fixture')
  expect(panel).toHaveTextContent(/Revision 2/)
  expect(panel).toHaveTextContent(/not proof of the settings in effect/)
  expect(panel).toHaveTextContent('Daily checkpoints at 06:15, 07:20')
  expect(panel).toHaveTextContent('3 stored records')
  expect(panel).toHaveTextContent('Completed calls only')
  expect(panel).toHaveTextContent('1 stored record')
  expect(panel).toHaveTextContent('combined-summary bit set (meaning depends on terminal profile)')
  expect(within(panel).getByRole('status')).toHaveTextContent('later download attempt')
  expect(screen.getByText('17')).toBeVisible()
  expect(screen.queryByRole('region', { name: 'MT35 call-record threshold reference' })).toBeNull()
})

it('shows normal and optional thresholds separately for a received call record', async () => {
  show(53)
  const optional = await screen.findByRole('region', { name: 'MT34 call and carrier collection reference' })
  expect(optional).toHaveTextContent('1 stored record')
  const normal = screen.getByRole('region', { name: 'MT35 call-record threshold reference' })
  expect(normal).toHaveTextContent('2 stored records')
  expect(normal).toHaveTextContent('An active MT34 optional logging window can select a different threshold.')
  expect(within(normal).getByRole('link', { name: 'View source download' })).toHaveAttribute('href', '/downloads/callin-fixture')
  expect(screen.getAllByText('25')).toHaveLength(2)
})

it('keeps original report counts visible without revealing restricted reference information', async () => {
  show(56, { ...collection, status: 'restricted' })
  const panel = await screen.findByRole('region', { name: 'MT34 call and carrier collection reference' })
  expect(panel).toHaveTextContent('Viewing this reference requires read access to Downloads and Table records.')
  expect(within(panel).queryByRole('link')).toBeNull()
  expect(panel).not.toHaveTextContent('Completed calls only')
  expect(screen.getByText('17')).toBeVisible()
})

it.each([37, 69, 153])('does not attach an MT34 reference to unrelated MT%s uploads', async type => {
  show(type)
  await screen.findByRole('heading', { name: 'Fixture statistics' })
  expect(screen.queryByRole('region', { name: 'MT34 call and carrier collection reference' })).toBeNull()
})
