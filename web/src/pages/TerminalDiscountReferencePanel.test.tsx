import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { TerminalDiscountReferencePanel } from './TerminalDiscountReferencePanel'

vi.mock('../api/client', () => ({ apiFetch: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

it('keeps errors and incompatible profiles distinct from a reviewed empty result', async () => {
  const empty = { discount: 1, setRevision: 2, profile: 'Later fixture', review: { supported: true, reason: null,
    references: [], reviewedRecords: [93, 134, 152], missingRecords: [], unreviewedRecords: [] } }
  vi.mocked(apiFetch).mockResolvedValueOnce(new Response(null, { status: 500 }))
    .mockResolvedValueOnce(Response.json({ ...empty, review: { ...empty.review, supported: false, reason: 'Select a verified later profile.' } }))
    .mockResolvedValueOnce(Response.json(empty))
  render(<TerminalDiscountReferencePanel setId="fixture" discount={1} dirty />)
  expect(screen.getByRole('note')).toHaveTextContent('Unsaved edits are not included')
  expect(apiFetch).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Review references' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('HTTP 500')
  fireEvent.click(screen.getByRole('button', { name: 'Review references' }))
  expect(await screen.findByRole('status')).toHaveTextContent('Select a verified later profile.')
  fireEvent.click(screen.getByRole('button', { name: 'Refresh references' }))
  expect(await screen.findByText(/This does not establish that the discount is unused/)).toBeVisible()
  expect(screen.getByText('Reviewed: MT93, MT134, MT152')).toBeVisible()
})
