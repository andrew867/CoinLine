import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { PaymentTransactionsPage } from './PaymentTransactionsPage'

vi.mock('../api/client', () => ({ apiGet: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

it('renders redacted request structure and a useful malformed result without implying payment', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'card-transactions': 1 })
  const common = { terminalId: 'fixture-phone', terminalDisplayName: 'Synthetic phone', nccSessionId: 'fixture-session',
    createdAtUtc: '2026-01-01T12:00:00Z', messageType: 2, provider: 'none', reason: null,
    cardAccountId: null, quoteId: null, quotedAmountUsd: null, callSequence: null }
  vi.mocked(apiGet).mockImplementation(async path => path.endsWith('authorization-attempts') ? [
    { ...common, id: 'valid', outcome: 'provider_unavailable', requestBytes: 147, bundledCallRecords: 2 },
    { ...common, id: 'invalid', outcome: 'malformed_request', requestBytes: 40, bundledCallRecords: null, suffixCdrTypes: [53] },
    { ...common, id: 'timeout', outcome: 'provider_timeout', provider: 'trusted_provider' },
    { ...common, id: 'http-error', outcome: 'provider_http_error', provider: 'trusted_provider' },
    { ...common, id: 'historical', outcome: 'unknown', provider: 'unrecorded' },
  ] as never : [] as never)
  render(<MemoryRouter><PaymentTransactionsPage /></MemoryRouter>)
  const history = await screen.findByRole('region', { name: 'Authorization history' })
  expect(within(history).getByText('147 application bytes · 2 bundled call records')).toBeVisible()
  expect(within(history).getByText('No provider')).toBeVisible()
  expect(within(history).getByText('Unrecognized request length or trailing record format')).toBeVisible()
  expect(within(history).getByText('40 application bytes')).toBeVisible()
  expect(within(history).getByText('Recognized suffix CDR types: MT53')).toBeVisible()
  expect(within(history).getByText('Provider timed out')).toBeVisible()
  expect(within(history).getByText('Provider HTTP error')).toBeVisible()
  expect(within(history).getByText('Outcome unavailable', { exact: true })).toBeVisible()
  expect(screen.getByText('No payment transactions recorded.')).toBeVisible()
  fireEvent.change(screen.getByLabelText('Authorization result'), { target: { value: 'approved' } })
  expect(screen.getByText('No attempts match this result filter.')).toBeVisible()
  expect(screen.getByText('No payment transactions recorded.')).toBeVisible()
})
