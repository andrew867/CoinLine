import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it } from 'vitest'
import { consoleSession } from '../api/consoleSession'
import { readDownloadReviews, rememberDownloadReview, resolveScheduledDownloadReview, saveDownloadReviewPrompt } from '../api/downloadReviews'
import { DownloadReviewFooter, PhoneDownloadReview } from './DownloadReviewReminder'

afterEach(() => { cleanup(); consoleSession.clear(); saveDownloadReviewPrompt(true) })
const review = { terminalId: 'fixture-phone', tableSetId: 'fixture-set', reason: 'assignment' as const }

it('reminds the operator without treating a preview or a different scheduled set as delivery', () => {
  rememberDownloadReview(review)
  render(<MemoryRouter><PhoneDownloadReview terminalId={review.terminalId} tableSetId={review.tableSetId} canSchedule canReadAssignment />
    <DownloadReviewFooter canReadPhones /></MemoryRouter>)
  expect(screen.getByRole('link', { name: 'Preview download for this phone' })).toHaveAttribute('href',
    '/table-sets/fixture-set?schedulePhone=fixture-phone#table-assignment-heading')
  expect(screen.getByRole('region', { name: 'Pending download reviews' })).toHaveTextContent('These are reminders, not queued downloads.')
  const exit = new Event('beforeunload', { cancelable: true })
  window.dispatchEvent(exit)
  expect(exit.defaultPrevented).toBe(true)
  resolveScheduledDownloadReview(review.terminalId, 'different-set')
  expect(readDownloadReviews()).toEqual([review])
  fireEvent.click(screen.getByRole('button', { name: 'Mark reviewed (no download needed)' }))
  expect(readDownloadReviews()).toEqual([])
  expect(screen.queryByRole('region', { name: 'Pending download reviews' })).toBeNull()
})

it('can turn off the immediate prompt, retains the tab reminder and clears it at sign-out', () => {
  saveDownloadReviewPrompt(false)
  rememberDownloadReview(review)
  render(<MemoryRouter><PhoneDownloadReview terminalId={review.terminalId} tableSetId={review.tableSetId} canSchedule canReadAssignment />
    <DownloadReviewFooter canReadPhones /></MemoryRouter>)
  expect(screen.queryByRole('region', { name: 'Download review' })).toBeNull()
  expect(screen.getByRole('region', { name: 'Pending download reviews' })).toBeVisible()
  consoleSession.clear()
  expect(readDownloadReviews()).toEqual([])
})

it('does not expose an assignment or schedule link to an operator without the necessary access', () => {
  rememberDownloadReview({ terminalId: 'fixture-phone', tableSetId: null, reason: 'registration' })
  render(<MemoryRouter><PhoneDownloadReview terminalId="fixture-phone" tableSetId={null} canSchedule={false} canReadAssignment={false} /></MemoryRouter>)
  expect(screen.getByText(/Ask an operator with the required assignment and download access/)).toBeVisible()
  expect(screen.queryByRole('link', { name: 'Preview download for this phone' })).toBeNull()
})

it('does not retain phone reviews across a different sign-in session', () => {
  consoleSession.save('first-session', '', undefined, 'Operator')
  rememberDownloadReview(review)
  consoleSession.save('different-session', '', undefined, 'Operator')
  expect(readDownloadReviews()).toEqual([])
})
