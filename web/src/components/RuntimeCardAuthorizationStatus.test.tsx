import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { RuntimeCardAuthorizationStatus } from './RuntimeCardAuthorizationStatus'
import { apiGet } from '../api/client'

vi.mock('../api/client', () => ({ apiGet: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

it('distinguishes missing authorizer from registered but unverified processor', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce({ runtimeAuthorizationProviderRegistered: false, physicalCardWritesDisabled: true })
  const first = render(<RuntimeCardAuthorizationStatus />)
  expect(await screen.findByText(/Terminal card authorizations will be declined/)).toBeVisible()
  expect(screen.getByText(/Physical card writes are disabled/)).toBeVisible()
  first.unmount()
  vi.mocked(apiGet).mockResolvedValueOnce({ runtimeAuthorizationProviderRegistered: true, physicalCardWritesDisabled: true })
  render(<RuntimeCardAuthorizationStatus />)
  expect(await screen.findByText(/Processor connectivity and settlement readiness are not established/)).toBeVisible()
})

it('does not guess authorization state on an old or unavailable host', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce({ physicalCardWritesDisabled: true })
  const first = render(<RuntimeCardAuthorizationStatus />)
  expect(await screen.findByText(/does not report its runtime authorization provider status/)).toBeVisible()
  first.unmount()
  vi.mocked(apiGet).mockRejectedValueOnce(new Error('unavailable'))
  render(<RuntimeCardAuthorizationStatus />)
  expect(await screen.findByText(/Authorization status could not be loaded/)).toBeVisible()
})

it('describes the opt-in provider as synthetic without promising settlement', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce({ runtimeAuthorizationProviderRegistered: true, labAuthorizationEnabled: true, physicalCardWritesDisabled: true })
  render(<RuntimeCardAuthorizationStatus />)
  expect(await screen.findByText(/Synthetic lab authorization configured/)).toBeVisible()
  expect(screen.getByText(/Only explicitly bound test accounts and local test routes/)).toBeVisible()
  expect(screen.getByText(/Physical card writes are disabled/)).toBeVisible()
})
