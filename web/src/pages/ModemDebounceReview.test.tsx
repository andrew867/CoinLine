import { cleanup, render, screen, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { ModemDebounceReview, modemDebounceMilliseconds } from './ModemDebounceReview'

afterEach(cleanup)

it('only calculates a nominal duration for nonzero valid byte counts', () => {
  expect(modemDebounceMilliseconds('1', '5')).toBe(50)
  expect(modemDebounceMilliseconds('1', '25')).toBe(250)
  expect(modemDebounceMilliseconds('2', '10')).toBe(200)
  expect(modemDebounceMilliseconds('0', '10')).toBeNull()
  expect(modemDebounceMilliseconds('1', '0')).toBeNull()
  expect(modemDebounceMilliseconds('1', '256')).toBeNull()
  expect(modemDebounceMilliseconds('1', '-1')).toBeNull()
  expect(modemDebounceMilliseconds('1', null)).toBeNull()
})

it('does not apply the later sampling assumption to an earlier profile', () => {
  const values: Record<string, string> = { 'debounce-2.interval': '1', 'debounce-2.off': '5', 'debounce-2.on': '10' }
  render(<ModemDebounceReview later={false} value={key => values[key]} onOpen={vi.fn()} />)
  const region = screen.getByRole('region', { name: 'Modem detector timing review' })
  const row = within(region).getByRole('row', { name: /Answer tone/ })
  expect(within(row).getByText('5')).toBeVisible()
  expect(within(region).queryByText('50 ms')).not.toBeInTheDocument()
  expect(within(region).queryByRole('columnheader', { name: 'Nominal release' })).not.toBeInTheDocument()
  expect(within(region).getByText(/sampling unit is not established/)).toBeVisible()
})
