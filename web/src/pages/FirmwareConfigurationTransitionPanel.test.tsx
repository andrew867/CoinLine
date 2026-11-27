import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiPostRaw } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { FirmwareConfigurationTransitionPanel } from './FirmwareConfigurationTransitionPanel'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiPostRaw: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); vi.restoreAllMocks(); consoleSession.clear() })

it('stages an explicit target version, waits for reported evidence, then activates the reviewed target batch', async () => {
  consoleSession.save('fixture-token', '', 'Fixture admin', 'Admin')
  vi.spyOn(window, 'confirm').mockReturnValue(true)
  const plan = { id: 'plan', revision: 0, state: 'staged', blocker: 'Wait for a fresh matched NCC report',
    sourceTableSetId: 'source', targetTableSetId: 'target', sourceControllerRom: 'LABOLD', sourceControllerSoftware: 'A001',
    targetControllerRom: 'LABNEW', targetControllerSoftware: 'A002', targetBatchId: null }
  let history: Record<string, unknown>[] = []
  vi.mocked(apiGet).mockImplementation(async (url) => url.includes('/options?') ? {
    observed: { rom: 'LABOLD', software: 'A001', capturedAtUtc: '2026-11-25T12:00:00Z' },
    schedules: [{ id: 'schedule', rom: 'LABNEW', application: 'B001', availableAtUtc: '2026-11-25T12:00:00Z', completedAtUtc: null }],
    candidates: [{ id: 'target', name: 'Prepared set', firmwareLabel: 'Later profile' }],
  } : history)
  vi.mocked(apiPostRaw).mockImplementation(async (url) => {
    if (url.endsWith('/preview')) return Response.json({ plan, reviewToken: 'reviewed' })
    if (url.endsWith('/activate')) { history = [{ ...plan, state: 'activated', blocker: null, targetBatchId: 'batch' }]; return Response.json(history[0]) }
    history = [plan]; return Response.json(plan, { status: 201 })
  })
  render(<MemoryRouter><FirmwareConfigurationTransitionPanel phones={[{ id: 'phone', displayName: 'Fixture phone' }]} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: 'Manage configuration transitions' }))
  fireEvent.change(await screen.findByLabelText('Published target configuration'), { target: { value: 'target' } })
  fireEvent.change(screen.getByLabelText('Existing enabled code schedule'), { target: { value: 'schedule' } })
  fireEvent.change(screen.getByLabelText('Expected controller software label (MT60)'), { target: { value: 'A002' } })
  fireEvent.click(screen.getByRole('button', { name: 'Preview configuration transition' }))
  expect(await screen.findByText('LABOLD / A001 to LABNEW / A002')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Stage reviewed configuration' }))
  expect(await screen.findByText('Wait for a fresh matched NCC report')).toBeVisible()
  expect(screen.getByRole('button', { name: 'Activate target configuration' })).toBeDisabled()
  expect(apiPostRaw).toHaveBeenNthCalledWith(2, '/api/firmware/configuration-transitions', expect.objectContaining({ expectedControllerSoftware: 'A002', reviewToken: 'reviewed' }))
  history = [{ ...plan, state: 'ready', blocker: null }]
  fireEvent.click(screen.getByRole('button', { name: 'Refresh transition status' }))
  expect(await screen.findByText('Ready for activation')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Activate target configuration' }))
  expect(await screen.findByRole('link', { name: 'Target download' })).toHaveAttribute('href', '/downloads/batch')
  expect(apiPostRaw).toHaveBeenNthCalledWith(3, '/api/firmware/configuration-transitions/plan/activate', { confirm: true, revision: 0 })
})

it('does not offer management actions to a nonadministrator even with a reported target', async () => {
  consoleSession.save('fixture-token', '', 'Fixture operator', 'Operator')
  vi.mocked(apiGet).mockResolvedValueOnce({ observed: null, schedules: [], candidates: [] })
    .mockResolvedValueOnce([{ id: 'plan', revision: 0, state: 'ready', sourceTableSetId: 'source', targetTableSetId: 'target' }])
  render(<MemoryRouter><FirmwareConfigurationTransitionPanel phones={[{ id: 'phone', displayName: 'Fixture phone' }]} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: 'Manage configuration transitions' }))
  expect(await screen.findByText('Ready for activation')).toBeVisible()
  expect(screen.queryByRole('button', { name: 'Activate target configuration' })).not.toBeInTheDocument()
  expect(screen.queryByRole('button', { name: 'Preview configuration transition' })).not.toBeInTheDocument()
})

it('requires explicit automatic approval and a fresh preview when the activation mode changes', async () => {
  consoleSession.save('fixture-token', '', 'Fixture admin', 'Admin')
  const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
  const plan = { id: 'plan', state: 'staged', revision: 0, blocker: 'Waiting for version', sourceTableSetId: 'source', targetTableSetId: 'target',
    sourceControllerRom: 'LABOLD', sourceControllerSoftware: 'A001', targetControllerRom: 'LABNEW', targetControllerSoftware: 'A002', targetBatchId: null }
  let history: Record<string, unknown>[] = []
  vi.mocked(apiGet).mockImplementation(async (url) => url.includes('/options?') ? {
    observed: null, schedules: [{ id: 'schedule', rom: 'LABNEW', application: 'B001', availableAtUtc: '2026-11-25T12:00:00Z', completedAtUtc: null }],
    candidates: [{ id: 'target', name: 'Target set', firmwareLabel: 'Later profile' }],
  } : history)
  vi.mocked(apiPostRaw).mockResolvedValueOnce(Response.json({ plan: { ...plan, autoActivateOnContact: false }, reviewToken: 'manual' }))
    .mockResolvedValueOnce(Response.json({ plan: { ...plan, autoActivateOnContact: true }, reviewToken: 'automatic' }))
    .mockImplementationOnce(async () => { history = [{ ...plan, autoActivateOnContact: true }]; return Response.json(history[0], { status: 201 }) })
  render(<MemoryRouter><FirmwareConfigurationTransitionPanel phones={[{ id: 'phone', displayName: 'Fixture phone' }]} /></MemoryRouter>)
  fireEvent.click(screen.getByRole('button', { name: 'Manage configuration transitions' }))
  fireEvent.change(await screen.findByLabelText('Published target configuration'), { target: { value: 'target' } })
  fireEvent.change(screen.getByLabelText('Existing enabled code schedule'), { target: { value: 'schedule' } })
  fireEvent.change(screen.getByLabelText('Expected controller software label (MT60)'), { target: { value: 'A002' } })
  const choice = screen.getByLabelText('Automatically activate on the first verified target-version contact')
  expect(choice).not.toBeChecked()
  fireEvent.click(screen.getByRole('button', { name: 'Preview configuration transition' }))
  expect(await screen.findByText('Manual administrator activation required.')).toBeVisible()
  fireEvent.click(choice)
  expect(screen.queryByRole('button', { name: 'Stage reviewed configuration' })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Preview configuration transition' }))
  expect(await screen.findByText('Automatic activation approved on matching target-version contact.')).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Stage reviewed configuration' }))
  expect(await screen.findByText('Automatic activation approved')).toBeVisible()
  expect(confirm).toHaveBeenCalledWith(expect.stringContaining('authorize automatic activation'))
  expect(apiPostRaw).toHaveBeenLastCalledWith('/api/firmware/configuration-transitions', expect.objectContaining({ autoActivateOnContact: true, reviewToken: 'automatic' }))
  history = [{ ...plan, autoActivateOnContact: true, activatedAutomatically: true, state: 'activated', blocker: null, targetBatchId: 'batch' }]
  fireEvent.click(screen.getByRole('button', { name: 'Refresh transition status' }))
  expect(await screen.findByText('Activated automatically')).toBeVisible()
})
