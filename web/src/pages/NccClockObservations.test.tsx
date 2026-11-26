import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet } from '../api/client'
import { NccSessionsPage } from './NccSessionsPage'
import { NccClockObservations } from './NccClockObservations'

vi.mock('../api/client', () => ({ apiGet: vi.fn(), apiDownload: vi.fn(), apiPostBareOk: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

it('opens a retained session and shows the exact original and retransmitted calendar', async () => {
  vi.mocked(apiGet).mockImplementation(async path => {
    if (path === '/api/ncc/sessions' || path === '/api/ncc/sessions?includeArchived=true&sessionId=fixture-session')
      return [{ id: 'fixture-session', terminalId: null, correlationId: 'Fixture session', status: 1,
        startedAtUtc: '2026-11-26T07:44:00Z', endedAtUtc: '2026-11-26T07:45:00Z', lastFrameSampleHex: null }] as never
    if (path === '/api/ncc/sessions/fixture-session?clockOnly=true') return { clockOffers: [
      { sequence: 3, offeredClockUtc: '2026-11-26T07:44:27Z', retransmission: false },
      { sequence: 5, offeredClockUtc: '2026-11-26T07:44:27Z', retransmission: true },
    ] } as never
    throw new Error('Unexpected request')
  })
  render(<MemoryRouter initialEntries={['/ncc-sessions']}><NccSessionsPage /></MemoryRouter>)
  fireEvent.click(await screen.findByRole('link', { name: 'View session' }))
  const observations = await screen.findByRole('region', { name: 'Clock synchronization observations' })
  expect(await within(observations).findByText('Original offer')).toBeVisible()
  expect(within(observations).getByText('Retransmitted')).toBeVisible()
  expect(within(observations).getAllByText('2026-11-26 07:44:27 Z')).toHaveLength(2)
  expect(within(observations).getByText(/not proof of the phone's stored clock/)).toBeVisible()
})

it('distinguishes older missing metadata from an API failure', async () => {
  vi.mocked(apiGet).mockResolvedValueOnce({}).mockRejectedValueOnce(new Error('Failed'))
  const { rerender } = render(<NccClockObservations sessionId="old" />)
  expect(await screen.findByText(/No retained clock offer/)).toBeVisible()
  rerender(<NccClockObservations sessionId="failed" />)
  expect(await screen.findByRole('alert')).toHaveTextContent('Could not load the clock synchronization observations.')
  expect(screen.queryByText(/No retained clock offer/)).not.toBeInTheDocument()
})
