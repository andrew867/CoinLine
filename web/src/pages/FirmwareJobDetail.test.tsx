import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiPost, apiPostRaw } from '../api/client'
import { FirmwareJobDetail } from './FirmwareJobDetail'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiPost: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

const base = {
  id: 'job-fixture', terminalId: 'terminal-fixture', firmwarePackageId: 'package-fixture', firmwareArtifactId: 'artifact-fixture',
  status: 4, simulationMode: false, safetyStateJson: '{}', approvedAtUtc: '2026-01-01T00:00:00Z',
  approvedByOperatorId: 'fixture', cancelReason: '', createdAtUtc: '2026-01-01T00:00:00Z', steps: [], safetyChecks: [],
  rollBackPlan: null, hardwareValidationNotice: 'Fixture policy'
}
const liveStep = { id: 'step', stepIndex: 100, name: 'live_payload_prepare', stepStatus: 2, succeeded: true, detail: 'Fixture' }

function open() {
  render(<MemoryRouter initialEntries={['/firmware/jobs/job-fixture']}><Routes>
    <Route path="/firmware/jobs/:id" element={<FirmwareJobDetail />} />
  </Routes></MemoryRouter>)
}

it('submits one live action and removes it after the attempt is retained', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce(base).mockResolvedValueOnce({ ...base, steps: [liveStep] })
  let finish!: (response: Response) => void
  vi.mocked(apiPostRaw).mockImplementation(() => new Promise(resolve => { finish = resolve }))
  open()
  const execute = await screen.findByRole('button', { name: /Execute live/ })
  fireEvent.click(execute)
  fireEvent.click(execute)
  expect(apiPostRaw).toHaveBeenCalledTimes(1)
  expect(execute).toBeDisabled()
  finish(new Response(JSON.stringify({ status: 4 }), { status: 200 }))
  expect(await screen.findByText(/A live attempt is retained/)).toBeVisible()
  expect(screen.queryByRole('button', { name: /Execute live/ })).not.toBeInTheDocument()
})

it('does not offer simulation, cancellation or another live execution for a claimed job', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce({ ...base, status: 3 })
  open()
  expect(await screen.findByText(/Live execution is claimed or its outcome is uncertain/)).toBeVisible()
  expect(screen.getByRole('button', { name: 'Run simulation' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Cancel job' })).toBeDisabled()
  expect(screen.queryByRole('button', { name: /Execute live/ })).not.toBeInTheDocument()
})

it('retains approval and disables the live action when the endpoint is not configured', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce({ ...base, liveExecutionConfigured: false })
  open()
  expect(await screen.findByText(/This approval is retained/)).toBeVisible()
  const execute = screen.getByRole('button', { name: /Execute live/ })
  expect(execute).toBeDisabled()
  fireEvent.click(execute)
  expect(apiPostRaw).not.toHaveBeenCalled()
})

it('refreshes the job after a lost response instead of assuming no attempt occurred', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce(base).mockResolvedValueOnce({ ...base, status: 3, steps: [liveStep] })
  vi.mocked(apiPostRaw).mockRejectedValueOnce(new Error('Connection ended'))
  open()
  fireEvent.click(await screen.findByRole('button', { name: /Execute live/ }))
  await waitFor(() => expect(apiGet).toHaveBeenCalledTimes(2))
  expect(await screen.findByText(/Live execution is claimed or its outcome is uncertain/)).toBeVisible()
  expect(screen.queryByRole('button', { name: /Execute live/ })).not.toBeInTheDocument()
})

it('approves the selected staged schedule and does not offer outbound execution for the bound job', async () => {
  const bound = { ...base, codeTransitionId: 'transition-fixture', codeScheduleId: 'schedule-fixture', incomingAttemptLimit: 2, incomingAttemptCount: 0 }
  vi.mocked(apiGet).mockResolvedValueOnce({ ...base, approvedAtUtc: null })
    .mockResolvedValueOnce([{ id: 'transition-fixture', codeScheduleId: 'schedule-fixture', sourceControllerRom: 'LABOLD',
      sourceControllerSoftware: 'A001', targetControllerRom: 'LABNEW', targetControllerSoftware: 'A002' }])
    .mockResolvedValueOnce(bound)
  vi.mocked(apiPost).mockResolvedValueOnce({ approved: true })
  open()
  const option = await screen.findByRole('option', { name: 'LABOLD A001 to LABNEW A002' })
  expect(option).toBeVisible()
  fireEvent.change(screen.getByLabelText('Delivery configuration'), { target: { value: 'transition-fixture' } })
  fireEvent.change(screen.getByLabelText('Maximum incoming attempts (including initial)'), { target: { value: '2' } })
  fireEvent.click(screen.getByRole('button', { name: 'Approve (after simulation)' }))
  await waitFor(() => expect(apiPost).toHaveBeenCalledWith('/api/firmware/jobs/job-fixture/approve',
    expect.objectContaining({ codeTransitionId: 'transition-fixture', maximumIncomingAttempts: 2 })))
  expect(await screen.findByText(/Approval bound to terminal-initiated delivery/)).toBeVisible()
  expect(screen.queryByRole('button', { name: /Execute live/ })).not.toBeInTheDocument()
  expect(screen.getByRole('button', { name: 'Cancel job' })).toBeDisabled()
  fireEvent.click(screen.getByLabelText('Confirm cancel (audited)'))
  expect(screen.getByRole('button', { name: 'Cancel job' })).toBeEnabled()
})

it('reports a confirmed incomplete attempt awaiting an exact callback and allows audited cancellation', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce({ ...base, status: 5, codeTransitionId: 'transition-fixture',
    codeScheduleId: 'schedule-fixture', incomingCodeServerListening: true, incomingAttemptLimit: 2,
    incomingAttemptCount: 1, incomingRetryEligible: true, steps: [{ ...liveStep, name: 'live_code_transport', succeeded: false }] })
  open()
  expect(await screen.findByText(/Waiting for terminal retry/)).toHaveTextContent('Attempts: 1/2')
  expect(screen.getByText('Did not complete', { exact: true })).toBeVisible()
  expect(screen.queryByRole('button', { name: /Execute live/ })).not.toBeInTheDocument()
  fireEvent.click(screen.getByLabelText('Confirm cancel (audited)'))
  expect(screen.getByRole('button', { name: 'Cancel job' })).toBeEnabled()
})

it('uses the latest delivery result after a successful repeated callback', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce({ ...base, codeTransitionId: 'transition-fixture', codeScheduleId: 'schedule-fixture',
    incomingAttemptLimit: 2, incomingAttemptCount: 2, incomingRetryEligible: false, steps: [
      { ...liveStep, name: 'live_code_transport', stepIndex: 101, succeeded: false },
      { ...liveStep, id: 'transport2', name: 'live_code_transport', stepIndex: 111, succeeded: true },
      { ...liveStep, id: 'response2', name: 'live_code_application_response', stepIndex: 112, succeeded: true }
    ] })
  open()
  expect(await screen.findByText(/Attempts: 2\/2/)).toBeVisible()
  expect(screen.getByText('Completed', { exact: true })).toBeVisible()
  expect(screen.getByText('Observed', { exact: true })).toBeVisible()
  expect(screen.queryByText(/Waiting for terminal retry/)).not.toBeInTheDocument()
})

it('reports completed incoming transport separately from an unobserved response and terminal service', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce({ ...base, codeTransitionId: 'transition-fixture', codeScheduleId: 'schedule-fixture',
    incomingCodeServerListening: true, steps: [
      { ...liveStep, name: 'live_code_transport' },
      { ...liveStep, id: 'response', name: 'live_code_application_response', stepIndex: 102, succeeded: false, stepStatus: 0 }
    ] })
  open()
  expect(await screen.findByRole('region', { name: 'Code delivery progress' })).toBeVisible()
  expect(screen.getByText('Completed', { exact: true })).toBeVisible()
  expect(screen.getByText('Not observed', { exact: true })).toBeVisible()
  expect(screen.getByText(/Separate evidence required/)).toBeVisible()
  expect(screen.queryByRole('button', { name: /Execute live/ })).not.toBeInTheDocument()
})
