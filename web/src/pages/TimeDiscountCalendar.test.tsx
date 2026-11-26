import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'
import { previewTimeDiscount, formatDiscountWindow, type DiscountZone, type DiscountPayment } from './timeDiscountPreview'
import { discountCalendarFixture } from './timeDiscountPreview.fixture'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

const evaluate = (date: string, time: string, zone: DiscountZone = 'local', payment: DiscountPayment = 'coin', overrides: Record<string, string> = {}) => {
  const values = { ...discountCalendarFixture().values, ...overrides }
  return previewTimeDiscount(key => values[key], date, time, zone, payment)
}

it('uses recurring holidays ahead of the weekday category and distinguishes leap dates', () => {
  expect(evaluate('2026-12-25', '00:00')).toMatchObject({ kind: 'calendar', day: 'Holidays', holidaySlots: [1], selection: { period: 1, discountIndex: 6, eligible: true } })
  expect(evaluate('2027-12-25', '23:59')).toMatchObject({ day: 'Holidays', selection: { period: 1 } })
  expect(evaluate('2028-02-29', '12:00')).toMatchObject({ day: 'Holidays', holidaySlots: [10] })
  expect(evaluate('2027-02-29', '12:00')).toMatchObject({ kind: 'unavailable' })
  expect(evaluate('2026-11-25', '10:00', 'local', 'coin', { 'holiday-3.month': '4', 'holiday-3.day': '31' })).toMatchObject({ invalidHolidaySlots: [4], day: 'Weekdays' })
})

it('honours inclusive start, exclusive end, and period precedence even when payment is disabled', () => {
  expect(evaluate('2026-11-25', '07:59')).toMatchObject({ selection: { kind: 'none' } })
  expect(evaluate('2026-11-25', '08:00')).toMatchObject({ day: 'Weekdays', overlap: true, selection: { period: 1, eligible: false } })
  expect(evaluate('2026-11-25', '11:30')).toMatchObject({ selection: { period: 1, discountIndex: 0, paymentMask: 2, eligible: false } })
  expect(evaluate('2026-11-25', '11:30', 'international', 'card')).toMatchObject({ selection: { period: 1, discountIndex: 8, eligible: true } })
  expect(evaluate('2026-11-25', '12:00')).toMatchObject({ selection: { period: 2, discountIndex: 1, eligible: true } })
  expect(evaluate('2026-11-25', '18:00')).toMatchObject({ selection: { kind: 'none' } })
})

it('evaluates overnight windows on the current category, not the prior day', () => {
  expect(evaluate('2026-11-28', '01:59')).toMatchObject({ day: 'Saturday', selection: { period: 1, eligible: true } })
  expect(evaluate('2026-11-28', '02:00')).toMatchObject({ selection: { kind: 'none' } })
  expect(evaluate('2026-11-28', '22:00')).toMatchObject({ day: 'Saturday', selection: { period: 1 } })
  expect(evaluate('2026-11-29', '01:00')).toMatchObject({ day: 'Sunday', selection: { kind: 'none' } })
  expect(evaluate('2026-11-29', '10:00')).toMatchObject({ day: 'Sunday', selection: { period: 1 } })
  expect(formatDiscountWindow({ start: 1320, end: 120 })).toBe('22:00 to 02:00 (overnight)')
  expect(formatDiscountWindow({ start: 0, end: 1440 })).toBe('00:00 to 24:00')
  expect(formatDiscountWindow({ start: 0, end: 0 })).toContain('inactive')
})

it('does not guess from unavailable, out-of-range, or unsupported imported values', () => {
  expect(evaluate('2026-11-25', '24:00')).toMatchObject({ kind: 'unavailable' })
  expect(evaluate('2026-11-25', '10:00', 'local', 'coin', { 'holiday-9.day': '' })).toMatchObject({ kind: 'unavailable' })
  expect(evaluate('2026-11-25', '13:00', 'local', 'coin', { 'period-0.endHour': '24', 'period-0.endMinute': '1' })).toMatchObject({ selection: { kind: 'unavailable' } })
  expect(evaluate('2026-11-25', '10:00', 'local', 'card', { 'period-0.localIndex': '15' })).toMatchObject({ selection: { kind: 'matched', discountIndex: 15, eligible: null } })
  expect(evaluate('2026-11-25', '10:00', 'local', 'card', { 'period-0.localPayment': '' })).toMatchObject({ selection: { eligible: null } })
})

it('recalculates a staged calendar edit and saves only the selected field', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  let { sections } = discountCalendarFixture()
  let revision = 1
  const saves: Record<string, string>[] = []
  vi.mocked(apiFetch).mockImplementation(async (path, init) => {
    if (init?.method === 'PUT') {
      const body = JSON.parse(init.body as string); saves.push(body.changes)
      sections = sections.map(section => ({ ...section, fields: section.fields.map(field => field.key in body.changes ? { ...field, value: body.changes[field.key] } : field) })); revision++
      return new Response(null, { status: 204 })
    }
    if (path.includes('/records/93/')) return Response.json({ supported: true, profile: 'Later packed profile', length: 225, revision: 7,
      sections: [{ fields: [ { key: 'discount-1.localMode', value: '1' }, { key: 'discount-1.localInitial', value: '80' }, { key: 'discount-1.localLater', value: '100' } ] }] })
    return Response.json({ supported: true, editable: true, profile: 'Later packed profile', length: 95, revision, sha256: `fixture-${revision}`, sections })
  })
  render(<MemoryRouter><NativeTableEditor setId="fixture" number={152} revision={1} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} /></MemoryRouter>)
  fireEvent.change(await screen.findByLabelText('Preview date'), { target: { value: '2026-11-25' } })
  fireEvent.change(screen.getByLabelText('Preview time'), { target: { value: '11:30' } })
  expect(screen.getByText('The selected period does not enable this payment. Period 2 is not a fallback.')).toBeVisible()
  expect(screen.getByRole('button', { name: 'Save record' })).toBeDisabled()
  fireEvent.change(screen.getByLabelText('Section or entry'), { target: { value: 'period-0' } })
  fireEvent.change(screen.getByLabelText('Ends at hour'), { target: { value: '11' } })
  expect(screen.getByText('The selected MT152 period enables this payment and references a discount row.')).toBeVisible()
  expect(screen.getAllByText('MT93 discount 2').filter(item => item.tagName === 'DD')).toHaveLength(1)
  expect(await screen.findByText('80% of normal charge')).toBeVisible()
  expect(screen.getByText('100% of normal charge')).toBeVisible()
  expect(screen.queryByText(/These periods overlap/)).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(saves).toEqual([{ 'period-0.endHour': '11' }]))
  await screen.findByText(/95 bytes · Revision 2/)
  expect(screen.getByText('The selected MT152 period enables this payment and references a discount row.')).toBeVisible()
  expect(screen.getByRole('button', { name: 'Save record' })).toBeDisabled()
})
