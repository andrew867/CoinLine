import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { NativeTableEditor } from './NativeTableEditor'
import { bucketRange, performanceBucketPlan } from './performanceBucketPlan'

vi.mock('../api/client', () => ({ apiFetch: vi.fn(), apiPostRaw: vi.fn(), apiGet: vi.fn(), apiPost: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks(); consoleSession.clear() })

it('uses seven consecutive half-open ranges, and puts each exact upper boundary into the next bucket', () => {
  const plan = performanceBucketPlan('100', '50')
  expect(plan.kind).toBe('valid')
  if (plan.kind !== 'valid') throw new Error('Expected valid ranges')
  expect(plan.buckets.map(bucket => bucketRange(bucket))).toEqual([
    'Below 1.00 s', '1.00 s to below 1.50 s', '1.50 s to below 2.00 s', '2.00 s to below 2.50 s',
    '2.50 s to below 3.00 s', '3.00 s to below 3.50 s', '3.50 s or more',
  ])
  for (const boundary of [100, 150, 200, 250, 300, 350]) {
    const matching = plan.buckets.filter(bucket => boundary >= bucket.lower && (bucket.upper === null || boundary < bucket.upper))
    expect(matching).toHaveLength(1)
    expect(matching[0].lower).toBe(boundary)
  }
})

it('keeps zero-width ranges empty and distinguishes valid limit, overflow and invalid imports', () => {
  const plan = performanceBucketPlan('0', '0')
  if (plan.kind !== 'valid') throw new Error('Expected valid zero-width ranges')
  expect(plan.zeroWidth).toBe(true)
  expect(plan.buckets.map(bucket => bucketRange(bucket))).toEqual([...Array(6).fill('Empty range'), '0.00 s or more'])
  const maximum = performanceBucketPlan('5', '13106')
  if (maximum.kind !== 'valid') throw new Error('Expected representable terminal boundary')
  expect(bucketRange(maximum.buckets[6])).toBe('655.35 s or more')
  expect(performanceBucketPlan('5', '13107')).toEqual({ kind: 'wrap' })
  for (const value of ['-1', '32768', '1.5', '', null]) expect(performanceBucketPlan(value, '10')).toEqual({ kind: 'invalid' })
})

it('previews a selected MT32 timer edit, warnings and other unchanged ranges, then saves only that timer', async () => {
  consoleSession.save('fixture', '', undefined, 'Operator', { 'table-records': 3 })
  const field = (key: string, label: string, value: string) => ({ key, label, value, editor: 'number', min: 0,
    max: 32767, maxLength: null, choices: null, help: null })
  let revision = 1, start = '100'
  const savedChanges: Record<string, string>[] = []
  vi.mocked(apiFetch).mockImplementation(async (_, init) => {
    if (init?.method === 'PUT') {
      const body = JSON.parse(init.body as string); savedChanges.push(body.changes); start = body.changes['interval-0.start'] ?? start; revision++
      return new Response(null, { status: 204 })
    }
    return Response.json({ supported: true, editable: true, profile: 'MTR 1.9 packed', length: 33, revision, sha256: `fixture-${revision}`,
      sections: [{ key: 'collection', label: 'Performance reporting', fields: [field('days', 'Collection interval (days)', '1')] },
        ...[[0, 'Access to dial complete', start, '50'], [1, 'Dial complete to carrier', '1000', '0'],
          [3, 'Carrier to first packet', '5', '13107'], [4, 'User waiting time', '-1', '100']].map(([index, label, first, interval]) => ({
          key: `interval-${index}`, label, fields: [field(`interval-${index}.start`, 'Start (10 ms ticks)', String(first)),
            field(`interval-${index}.duration`, 'Interval (10 ms ticks)', String(interval))],
        }))] })
  })
  render(<MemoryRouter><NativeTableEditor setId="fixture" number={32} revision={1} exchangeFile={null}
    onExchangeFile={vi.fn()} onSaved={vi.fn()} onDirty={vi.fn()} /></MemoryRouter>)
  const summary = await screen.findByText('Performance histogram bucket ranges (MT37)')
  summary.closest('details')!.open = true
  const measure = screen.getByRole('region', { name: 'Access to dial complete' })
  expect(within(measure).getByText('3.50 s or more')).toBeVisible()
  expect(within(screen.getByRole('region', { name: 'Dial complete to carrier' })).getAllByText('Empty range')).toHaveLength(5)
  expect(within(screen.getByRole('region', { name: 'Carrier to first packet' })).getByRole('status')).toHaveTextContent('wraps')
  expect(within(screen.getByRole('region', { name: 'User waiting time' })).getByRole('status')).toHaveTextContent('cannot be calculated')
  fireEvent.change(screen.getByLabelText('Section or entry'), { target: { value: 'interval-0' } })
  fireEvent.change(screen.getByLabelText('Start (10 ms ticks)'), { target: { value: '150' } })
  expect(within(measure).getByText('4.00 s or more')).toBeVisible()
  fireEvent.change(screen.getByLabelText('Section or entry'), { target: { value: 'collection' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save record' }))
  await waitFor(() => expect(savedChanges).toEqual([{ 'interval-0.start': '150' }]))
  await waitFor(() => {
    screen.getByText('Performance histogram bucket ranges (MT37)').closest('details')!.open = true
    expect(within(screen.getByRole('region', { name: 'Access to dial complete' })).getByText('4.00 s or more')).toBeVisible()
    expect(screen.getByRole('button', { name: 'Save record' })).toBeDisabled()
  })
})
