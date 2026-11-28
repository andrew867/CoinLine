import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet, apiPost } from '../api/client'
import { FirmwareJobsPage } from './FirmwareJobsPage'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiPost: vi.fn() }))
vi.mock('./FirmwareTransitionReviewPanel', () => ({ FirmwareTransitionReviewPanel: () => null }))
vi.mock('./FirmwareConfigurationTransitionPanel', () => ({ FirmwareConfigurationTransitionPanel: () => null }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

function setup(allowLiveFlashing: boolean, liveDlaEnabled: boolean) {
  vi.mocked(apiGet).mockImplementation(async path => {
    if (path === '/api/firmware/execution-policy') return { allowLiveFlashing, liveDlaEnabled, liveTransportConfigured: false } as never
    if (path === '/api/terminals') return [{ id: 'fixture-phone', displayName: 'Fixture phone' }] as never
    if (path === '/api/firmware/packages') return [{ id: 'fixture-package', name: 'Fixture package' }] as never
    if (path === '/api/firmware/jobs') return [] as never
    throw new Error('Unexpected read')
  })
  vi.mocked(apiPost).mockResolvedValue({ id: 'fixture-job' } as never)
  render(<MemoryRouter><FirmwareJobsPage /></MemoryRouter>)
}

it.each([[false, false], [true, false], [false, true]])('keeps public and incomplete policy jobs simulation-only', async (policy, dla) => {
  setup(policy, dla)
  await screen.findByRole('option', { name: 'Fixture phone' })
  expect(screen.queryByRole('checkbox', { name: /Prepare for live execution/ })).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Queue simulation job' }))
  await waitFor(() => expect(apiPost).toHaveBeenCalledWith('/api/terminals/fixture-phone/firmware-jobs', { firmwarePackageId: 'fixture-package', simulationMode: true }))
})

it('permits explicit live-capable preparation with both gates enabled but does not start delivery', async () => {
  setup(true, true)
  const prepare = await screen.findByRole('checkbox', { name: /Prepare for live execution/ })
  await screen.findByRole('option', { name: 'Fixture phone' })
  expect(prepare).not.toBeChecked()
  fireEvent.click(prepare)
  expect(screen.getByText(/No terminal contact or code transfer is started/)).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Prepare live-capable job' }))
  await waitFor(() => expect(apiPost).toHaveBeenCalledWith('/api/terminals/fixture-phone/firmware-jobs', { firmwarePackageId: 'fixture-package', simulationMode: false }))
  expect(apiPost).toHaveBeenCalledTimes(1)
  expect(await screen.findByRole('button', { name: 'Queue simulation job' })).toBeVisible()
  expect(prepare).not.toBeChecked()
})
