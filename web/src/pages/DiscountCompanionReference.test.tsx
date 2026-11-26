import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { DiscountCompanionReference, readDiscountCompanion } from './DiscountCompanionReference'

vi.mock('../api/client', () => ({ apiFetch: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

const field = (key: string, value: string | null) => ({ key, value })
const discountRecord = { supported: true as const, profile: 'Later packed profile', length: 225, revision: 4, sections: [{ fields: [
  field('discount-0.localMode', '0'), field('discount-0.localInitial', '-14'), field('discount-0.localLater', '3'),
  field('discount-0.intraMode', '1'), field('discount-0.intraInitial', '80'), field('discount-0.intraLater', '125'),
  field('discount-1.localMode', '2'), field('discount-1.localInitial', '127'), field('discount-1.localLater', '99'),
  field('discount-2.localMode', '3'), field('discount-2.localInitial', '255'), field('discount-2.localLater', '255'),
  field('discount-3.localMode', '2'), field('discount-3.localInitial', '128'),
  field('discount-4.localMode', '0'), field('discount-4.localInitial', null), field('discount-4.localLater', '128'),
] }] }
const tariffRecord = { supported: true as const, profile: 'Later packed profile', length: 1192, revision: 7, sections: [{ fields: [
  { ...field('rate-127.classification', '6'), choices: [{ value: 6, label: 'Inter-area toll' }] },
  field('rate-127.initialPeriod', '-1'), field('rate-127.initialCharge', '25'),
  field('rate-127.additionalPeriod', '60'), field('rate-127.additionalCharge', '5'),
] }] }

it('preserves the stored meaning of each discount mode, rejecting unsupported shapes rather than guessing', () => {
  expect(readDiscountCompanion(discountRecord, 0, 'local')).toEqual({ mode: 0, first: -14, later: 3, rate: null })
  expect(readDiscountCompanion(discountRecord, 0, 'intra')).toEqual({ mode: 1, first: 80, later: 125, rate: null })
  expect(readDiscountCompanion(discountRecord, 1, 'local')).toEqual({ mode: 2, first: null, later: null, rate: 127 })
  expect(readDiscountCompanion(discountRecord, 2, 'local')).toEqual({ mode: 3, first: null, later: null, rate: null })
  expect(readDiscountCompanion(discountRecord, 3, 'local')?.rate).toBeNull()
  expect(readDiscountCompanion(discountRecord, 4, 'local')).toEqual({ mode: 0, first: null, later: null, rate: null })
  expect(readDiscountCompanion({ ...discountRecord, profile: 'MTR 1.9 packed' }, 0, 'local')).toBeNull()
  expect(readDiscountCompanion(discountRecord, 10, 'local')).toBeNull()
})

it('reads same-set discount and replacement tariff settings, without treating them as a call quote', async () => {
  vi.mocked(apiFetch).mockImplementation(async path => Response.json(path.includes('/records/73/') ? tariffRecord : discountRecord))
  const { rerender } = render(<DiscountCompanionReference setId="fixture" revision={2} index={0} zone="local" />)
  const panel = await screen.findByRole('region', { name: 'Saved discount reference' })
  expect(await within(panel).findByText('-14 cents')).toBeVisible()
  expect(within(panel).getByText('+3 cents')).toBeVisible()
  expect(vi.mocked(apiFetch).mock.calls.some(([path]) => path.includes('/records/73/'))).toBe(false)
  rerender(<DiscountCompanionReference setId="fixture" revision={2} index={0} zone="intra" />)
  expect(within(panel).getByText('80% of normal charge')).toBeVisible()
  expect(within(panel).getByText('125% of normal charge')).toBeVisible()
  rerender(<DiscountCompanionReference setId="fixture" revision={2} index={1} zone="local" />)
  const tariff = await screen.findByRole('region', { name: 'Saved replacement tariff' })
  expect(tariff).toHaveTextContent('MT73 rate 127 · revision 7')
  expect(tariff).toHaveTextContent('Inter-area toll')
  expect(tariff).toHaveTextContent('Unlimited')
  expect(tariff).toHaveTextContent('25 cents')
  expect(tariff).toHaveTextContent('60 s')
  expect(tariff).toHaveTextContent('5 cents')
  expect(panel).toHaveTextContent('not a calculated call charge')
  expect(vi.mocked(apiFetch).mock.calls.map(([path]) => path)).toEqual(['/api/tables/sets/fixture/records/93/fields', '/api/tables/sets/fixture/records/73/fields'])
  rerender(<DiscountCompanionReference setId="fixture" revision={2} index={2} zone="local" />)
  expect(panel).toHaveTextContent('This referenced zone is inactive')
  expect(screen.queryByRole('region', { name: 'Saved replacement tariff' })).toBeNull()
})

it('distinguishes a missing companion from an API error and allows an explicit refresh', async () => {
  vi.mocked(apiFetch).mockResolvedValueOnce(Response.json({ supported: false, reason: 'Import a complete record to open its editor.' }))
    .mockResolvedValueOnce(new Response(null, { status: 403 })).mockResolvedValueOnce(Response.json(discountRecord))
  render(<DiscountCompanionReference setId="fixture" revision={1} index={0} zone="local" />)
  expect(await screen.findByText(/No compatible saved MT93 reference/)).toBeVisible()
  fireEvent.click(screen.getByRole('button', { name: 'Refresh saved references' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Could not load saved MT93: HTTP 403.')
  fireEvent.click(screen.getByRole('button', { name: 'Refresh saved references' }))
  expect(await screen.findByText('-14 cents')).toBeVisible()
})
