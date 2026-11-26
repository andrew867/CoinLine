import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { Link, MemoryRouter, Route, Routes } from 'react-router-dom'
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

it('links a nonblocking provisioning finding to the matching carrier editor', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-sets': 3, 'table-records': 3 })
  vi.mocked(apiGet).mockResolvedValue([])
  vi.mocked(apiFetch).mockImplementation(async () => Response.json({ id: 'set-fixture', name: 'Fixture set', status: 0,
    revision: 1, optionalRecordRemovals: [], firmwareFindings: [], provisioning: {
      operatingType: 'Multi-pay', acceptedCoinSlots: null, configuredCardRanges: null, onHookAdvertising: null,
      findings: [{ tableNumber: 135, message: 'Carrier reference 180 is assigned to different rows.' }],
    }, versions: [{ id: 'record-135', tableNumber: 135, tableRevision: 1, definitionName: 'Carrier screens',
      payloadLengthBytes: 1109, payloadSha256Hex: 'fixture-hash', validationPassed: true, configurationId: null, warnings: [] }] }))

  render(<MemoryRouter initialEntries={['/table-sets/set-fixture']}>
    <Routes><Route path="/table-sets/:id" element={<TableSetDetail />} /></Routes>
  </MemoryRouter>)

  expect(await screen.findByText(/reference 180 is assigned to different rows/)).toBeVisible()
  fireEvent.click(screen.getByRole('link', { name: 'Review MT135' }))
  expect(await screen.findByRole('heading', { name: 'Configuration editor MT135' })).toBeVisible()
})

it('does not expose the old set editor while the replacement set is loading', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-sets': 3, 'table-records': 3 })
  vi.mocked(apiGet).mockResolvedValue([])
  const details = (id: string, name: string) => Response.json({
    id, name, status: 0, revision: 1, optionalRecordRemovals: [], firmwareFindings: [], versions: [],
  })
  let finishReplacement!: (value: Response) => void
  vi.mocked(apiFetch).mockImplementation(async path => path === '/api/tables/sets/old-set'
    ? details('old-set', 'Old configuration')
    : new Promise(resolve => { finishReplacement = resolve }))
  render(<MemoryRouter initialEntries={['/table-sets/old-set?record=92']}>
    <Link to="/table-sets/replacement-set?record=92">Replacement</Link>
    <Routes><Route path="/table-sets/:id" element={<TableSetDetail />} /></Routes>
  </MemoryRouter>)
  expect(await screen.findByRole('heading', { name: 'Table set: Old configuration' })).toBeVisible()
  expect(screen.getByRole('heading', { name: 'Configuration editor MT92' })).toBeVisible()
  fireEvent.click(screen.getByRole('link', { name: 'Replacement' }))
  expect(screen.queryByRole('heading', { name: 'Table set: Old configuration' })).toBeNull()
  expect(screen.queryByRole('heading', { name: 'Configuration editor MT92' })).toBeNull()
  expect(screen.getByText('Loading…')).toBeVisible()
  await act(async () => { finishReplacement(details('replacement-set', 'Replacement configuration')) })
  expect(await screen.findByRole('heading', { name: 'Table set: Replacement configuration' })).toBeVisible()
  expect(screen.getByRole('heading', { name: 'Configuration editor MT92' })).toBeVisible()
})

it('ignores a late response from the previous set after navigating', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-sets': 1 })
  vi.mocked(apiGet).mockResolvedValue([])
  const details = (id: string, name: string) => Response.json({
    id, name, status: 0, revision: 1, optionalRecordRemovals: [], firmwareFindings: [], versions: [],
  })
  let finishOld!: (value: Response) => void
  vi.mocked(apiFetch).mockImplementation(async path => path === '/api/tables/sets/old-set'
    ? new Promise(resolve => { finishOld = resolve }) : details('replacement-set', 'Replacement configuration'))
  render(<MemoryRouter initialEntries={['/table-sets/old-set']}>
    <Link to="/table-sets/replacement-set">Replacement</Link>
    <Routes><Route path="/table-sets/:id" element={<TableSetDetail />} /></Routes>
  </MemoryRouter>)
  fireEvent.click(screen.getByRole('link', { name: 'Replacement' }))
  expect(await screen.findByRole('heading', { name: 'Table set: Replacement configuration' })).toBeVisible()
  await act(async () => { finishOld(details('old-set', 'Old configuration')) })
  expect(screen.getByRole('heading', { name: 'Table set: Replacement configuration' })).toBeVisible()
  expect(screen.queryByRole('heading', { name: 'Table set: Old configuration' })).toBeNull()
})
