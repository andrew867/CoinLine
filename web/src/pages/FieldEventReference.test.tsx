import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { FieldEventReference } from './FieldEventReference'

vi.mock('../api/client', () => ({ apiFetch: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

const fixture = {
  alarms: [{ code: 22, description: 'Memory lost' }, { code: 31, description: 'Coin jam' }],
  maintenance: [{ code: 996, description: 'Technician entered maintenance mode' }],
}
const success = () => new Response(JSON.stringify(fixture), { status: 200 })

it('loads the canonical reference on demand and searches names and numbers', async () => {
  vi.mocked(apiFetch).mockResolvedValue(success())
  render(<FieldEventReference />)
  expect(apiFetch).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Show reference' }))
  expect(await screen.findByText('Memory lost')).toBeVisible()
  expect(apiFetch).toHaveBeenCalledWith('/api/field-events/reference', expect.objectContaining({ signal: expect.any(AbortSignal) }))
  expect(within(screen.getByRole('region', { name: 'Maintenance opcodes' })).getByText('996')).toBeVisible()
  fireEvent.change(screen.getByRole('searchbox'), { target: { value: '22' } })
  expect(screen.getByText('Memory lost')).toBeVisible()
  expect(screen.queryByText('Coin jam')).toBeNull()
  fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'TECHNICIAN' } })
  expect(screen.queryByText('Memory lost')).toBeNull()
  expect(screen.getByText('Technician entered maintenance mode')).toBeVisible()
  fireEvent.change(screen.getByRole('searchbox'), { target: { value: 'unlisted' } })
  expect(screen.getAllByText('No matching codes.')).toHaveLength(2)
})

it('shows a failed request instead of an empty reference, and retries', async () => {
  vi.mocked(apiFetch).mockResolvedValueOnce(new Response('', { status: 500 })).mockResolvedValueOnce(success())
  render(<FieldEventReference />)
  fireEvent.click(screen.getByRole('button', { name: 'Show reference' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Could not load the reference.')
  expect(screen.queryByText('No matching codes.')).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
  expect(await screen.findByText('Memory lost')).toBeVisible()
  expect(screen.queryByRole('alert')).toBeNull()
})
