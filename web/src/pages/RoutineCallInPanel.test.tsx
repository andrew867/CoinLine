import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiPost } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { RoutineCallInPanel } from './RoutineCallInPanel'
import { NccSessionsPage } from './NccSessionsPage'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiPost: vi.fn(), apiDownload: vi.fn(), apiPostBareOk: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

const settings = {
  startDate: '26-11-25', startTime: '02:30:00', intervalDays: 1, intervalHours: 0, intervalMinutes: 0,
  retryMinutes: 5, retrySeconds: 0, recordThreshold: 30, adjustmentAt: null, adjustmentScheduled: false,
  adjustmentSeconds: 0, findings: [],
}
const review = {
  assigned: { status: 'assigned', tableSetId: 'fixture-set', tableSetName: 'Fixture configuration', tableRevision: 2,
    settings: { ...settings, startTime: '05:10:00' } },
  acknowledged: { status: 'acknowledged', tableSetId: 'prior-set', tableRevision: 1, downloadBatchId: 'fixture-batch',
    acknowledgedAtUtc: '2026-11-25T03:00:00Z', settings },
  assignmentMatchesAcknowledgement: false, laterUncertainAttempt: true, pendingBatches: 1,
  contact: { status: 'observed', sessionId: 'fixture-session', receivedAtUtc: '2026-11-25T04:00:00Z' },
}
function Location() { const location = useLocation(); return <p>Location {location.pathname}{location.search}{location.hash}</p> }
function show(active = true) {
  return render(<MemoryRouter initialEntries={['/terminals/fixture-phone']}><Routes>
    <Route path="/terminals/:id" element={<RoutineCallInPanel terminalId="fixture-phone" assignmentKey="fixture-set" terminalActive={active} />} />
    <Route path="/table-sets/:id" element={<Location />} />
  </Routes></MemoryRouter>)
}

it('compares intended and acknowledged times, source records, and uncertainty without inferring a next UTC time', async () => {
  consoleSession.save('fixture', '', 'Fixture', 'Operator', { 'table-assignments': 1, 'table-records': 3, 'table-sets': 5, downloads: 1, 'ncc-sessions': 1 })
  vi.mocked(apiGet).mockResolvedValue(review)
  show()
  const comparison = await screen.findByRole('region', { name: 'Call-in settings comparison' })
  const routine = within(comparison).getByRole('row', { name: /Routine time/ })
  expect(within(routine).getAllByRole('cell').map(cell => cell.textContent)).toEqual(['05:10:00', '02:30:00'])
  expect(screen.getByText('The assigned and last acknowledged MT35 bytes differ.')).toBeInTheDocument()
  expect(screen.getByRole('alert')).toHaveTextContent('A later MT35 attempt has no confirmed acknowledgement.')
  expect(screen.getByText('1 prepared or running batch with an unacknowledged MT35.')).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Open acknowledging download' })).toHaveAttribute('href', '/downloads/fixture-batch')
  expect(screen.getByRole('link', { name: 'Open contact' })).toHaveAttribute('href', '/ncc-sessions?sessionId=fixture-session')
  expect(screen.getByRole('link', { name: "Randomize this phone's call-in time" })).toHaveAttribute('href',
    '/table-sets/fixture-set?randomizePhone=fixture-phone#table-assignment-heading')
  expect(screen.getByText(/No next-contact UTC time is inferred/)).toBeInTheDocument()
  expect(apiGet).toHaveBeenCalledWith('/api/terminals/fixture-phone/routine-call-in')
  fireEvent.click(screen.getByText('Create a separate draft for call-in changes'))
  fireEvent.change(screen.getByLabelText('Draft name'), { target: { value: 'One phone call-ins' } })
  vi.mocked(apiPost).mockResolvedValue({ id: 'copy-set' })
  fireEvent.click(screen.getByRole('button', { name: 'Create MT35 draft' }))
  expect(await screen.findByText('Location /table-sets/copy-set?record=35&schedulePhone=fixture-phone&scheduleRecord=35#config-heading')).toBeInTheDocument()
  expect(apiPost).toHaveBeenCalledExactlyOnceWith('/api/tables/sets/fixture-set/clone', { name: 'One phone call-ins' })
})

it('shows restricted sources without creating links or offering a copy', async () => {
  consoleSession.save('fixture', '', 'Fixture', 'Operator', { 'table-assignments': 1, 'table-records': 1 })
  vi.mocked(apiGet).mockResolvedValue({ ...review, acknowledged: { status: 'restricted' }, contact: { status: 'restricted' },
    assignmentMatchesAcknowledgement: null, laterUncertainAttempt: false, pendingBatches: null })
  show()
  expect(await screen.findAllByText('Not available with your permissions')).toHaveLength(2)
  expect(screen.queryByRole('link', { name: 'Open acknowledging download' })).toBeNull()
  expect(screen.queryByRole('link', { name: 'Open contact' })).toBeNull()
  expect(screen.queryByRole('link', { name: "Randomize this phone's call-in time" })).toBeNull()
  expect(screen.queryByText('Create a separate draft for call-in changes')).toBeNull()
})

it('opens a retained archived session using the narrow session filter', async () => {
  vi.mocked(apiGet).mockResolvedValue([{ id: 'fixture-session', terminalId: null, correlationId: 'fixture-contact', status: 2,
    startedAtUtc: '2026-11-25T04:00:00Z', endedAtUtc: null, lastFrameSampleHex: null }])
  render(<MemoryRouter initialEntries={['/ncc-sessions?sessionId=fixture-session']}><NccSessionsPage /></MemoryRouter>)
  await screen.findByText('fixture-contact')
  expect(apiGet).toHaveBeenCalledExactlyOnceWith('/api/ncc/sessions?includeArchived=true&sessionId=fixture-session')
  expect(screen.getByRole('link', { name: 'Show all sessions' })).toHaveAttribute('href', '/ncc-sessions')
})
