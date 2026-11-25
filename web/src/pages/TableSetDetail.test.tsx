import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch, apiGet } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { TableSetDetail } from './TableSetDetail'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn(), apiPostBareOk: vi.fn(), apiPut: vi.fn() }))
vi.mock('./TableSetConfiguration', () => ({ TableSetConfiguration: ({ initialRecord }: { initialRecord: number }) =>
  <h2>Configuration editor MT{initialRecord}</h2> }))
vi.mock('./TableRecordCopy', () => ({ TableRecordCopy: () => null }))

afterEach(() => {
  cleanup()
  vi.resetAllMocks()
  consoleSession.clear()
})

it('blocks publishing an invalid numbering plan and opens the affected editor directly from its finding', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-sets': 3, 'table-records': 3 })
  vi.mocked(apiGet).mockResolvedValue([])
  vi.mocked(apiFetch).mockImplementation(async () => Response.json({ id: 'set-fixture', name: 'Fixture set', status: 0,
    revision: 1, optionalRecordRemovals: [], firmwareFindings: [{ tableNumber: 62, blocking: true, message: 'Dialing rules contain a circular match or fallback path.' }],
    versions: [{ id: 'record-62', tableNumber: 62, tableRevision: 1, definitionName: 'Numbering plan',
      payloadLengthBytes: 353, payloadSha256Hex: 'fixture-hash', validationPassed: true, configurationId: null, warnings: [] }] }))

  render(<MemoryRouter initialEntries={['/table-sets/set-fixture']}>
    <Routes><Route path="/table-sets/:id" element={<TableSetDetail />} /></Routes>
  </MemoryRouter>)

  const review = await screen.findByRole('link', { name: 'Review MT62' })
  expect(screen.getByRole('button', { name: 'Publish set' })).toBeDisabled()
  expect(screen.getByText(/circular match or fallback path/)).toBeVisible()
  fireEvent.click(review)
  expect(await screen.findByRole('heading', { name: 'Configuration editor MT62' })).toBeVisible()
})
