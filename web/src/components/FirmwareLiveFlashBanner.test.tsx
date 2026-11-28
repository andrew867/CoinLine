import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { FirmwareLiveFlashBanner } from './FirmwareLiveFlashBanner'
import { apiGet } from '../api/client'

vi.mock('../api/client', () => ({ apiGet: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

it('retains the public default and incomplete-gate warning', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce({ allowLiveFlashing: false, liveDlaEnabled: false,
    transportMode: 'simulation', liveTransportConfigured: false, hardwareValidationNotice: 'Default notice' })
  const first = render(<FirmwareLiveFlashBanner />)
  expect(await screen.findByText('Live firmware update disabled')).toBeVisible()
  expect(screen.getByRole('alert')).toHaveTextContent('HARDWARE_VALIDATION_REQUIRED')
  first.unmount()
  vi.mocked(apiGet).mockResolvedValueOnce({ allowLiveFlashing: true, liveDlaEnabled: false, hardwareValidationNotice: 'Default notice' })
  render(<FirmwareLiveFlashBanner />)
  expect(await screen.findByText('Live firmware update disabled')).toBeVisible()
  expect(screen.getByRole('alert')).toHaveTextContent('AllowLiveFlashing is on; live DLA is off')
})

it('reports enabled lab switches without claiming simulation is live', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce({ allowLiveFlashing: true, liveDlaEnabled: true,
    transportMode: 'simulation', liveTransportConfigured: false, hardwareValidationNotice: 'Old warning' })
  render(<FirmwareLiveFlashBanner />)
  expect(await screen.findByText('Live execution policy enabled')).toBeVisible()
  expect(screen.getByRole('status')).toHaveTextContent('Outbound transport: simulation.')
  expect(screen.queryByText(/no live endpoint is configured/)).not.toBeInTheDocument()
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  expect(screen.queryByText(/Old warning/)).not.toBeInTheDocument()
})

it('identifies a listening incoming code server independently of simulated outbound transport', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce({ allowLiveFlashing: true, liveDlaEnabled: true,
    transportMode: 'simulation', liveTransportConfigured: false, incomingCodeServerConfigured: true,
    incomingCodeServerListening: true, hardwareValidationNotice: '' })
  render(<FirmwareLiveFlashBanner />)
  expect(await screen.findByText(/Terminal-initiated code server is listening/)).toBeVisible()
  expect(screen.getByRole('status')).toHaveTextContent('Outbound/manual transport remains simulated.')
  expect(screen.queryByText(/Incoming delivery is not configured/)).not.toBeInTheDocument()
})

it('does not describe a configured but stopped listener as available', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce({ allowLiveFlashing: true, liveDlaEnabled: true,
    transportMode: 'simulation', liveTransportConfigured: false, incomingCodeServerConfigured: true,
    incomingCodeServerListening: false, hardwareValidationNotice: '' })
  render(<FirmwareLiveFlashBanner />)
  expect(await screen.findByText(/code server is configured but not listening/)).toBeVisible()
  expect(screen.queryByText(/code server is listening/)).not.toBeInTheDocument()
})

it('distinguishes configured transport settings from approval and compatibility', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce({ allowLiveFlashing: true, liveDlaEnabled: true,
    transportMode: 'tcp', liveTransportConfigured: true, hardwareValidationNotice: '' })
  render(<FirmwareLiveFlashBanner />)
  expect(await screen.findByText(/TCP transport settings are present/)).toBeVisible()
  expect(screen.getByRole('status')).toHaveTextContent('Explicit job approval and terminal compatibility are separate')
})

it('does not infer live transport from an older policy response', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce({ allowLiveFlashing: true, liveDlaEnabled: true, hardwareValidationNotice: '' })
  render(<FirmwareLiveFlashBanner />)
  expect(await screen.findByText(/Transport configuration is not reported/)).toBeVisible()
})
