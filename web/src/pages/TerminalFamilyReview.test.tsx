import { cleanup, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet } from '../api/client'
import { TerminalFamilyReview } from './TerminalFamilyReview'

vi.mock('../api/client', () => ({ apiGet: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

const screeningNote = 'The record has competing packed capacities. Confirm its native layout before treating it as the expected screening list.'
const promptNote = 'The target catalogue identifies MT85 and MT86 as a prompt pair. This set includes 1 of those two known compatible records.'

function setup(recordDirty: boolean) {
  vi.mocked(apiGet).mockImplementation(async path => {
    if (path === '/api/terminals/families') return [{ id: 'mtr20-desk', release: 'MTR 2.0', variant: 'Desk', maximizerTypeNumbers: [37] }] as never
    if (path === '/api/tables/sets/fixture/family-review?familyId=mtr20-desk') return {
      familyId: 'mtr20-desk', familyLabel: 'MTR 2.0 Desk', documented: true, screeningEntries: 200,
      operatingMode: { expectedCode: 1, expectedLabel: 'Card only', configuredCode: 2, configuredLabel: 'Multi-pay', status: 'differs',
        message: 'Review whether the payment-mode difference is intentional for this phone.' },
      note: 'Shared Desk checklist, not an independently qualified Desk wire profile.', tables: [
        { name: 'Call screening', required: true, candidates: [59, 92], status: 'review', records: [
          { number: 59, length: 1021, format: 'recognized', screeningSlots: [60, 85], screeningFit: 'ambiguous', screeningNote },
        ] },
        { name: 'Expanded visual prompts', required: true, candidates: [85, 86], status: 'partial', note: promptNote, records: [
          { number: 85, length: 8001, format: 'catalogueLength', screeningSlots: [] },
        ] },
        { name: 'Terminal feature and call options', required: true, candidates: [26], status: 'present', records: [
          { number: 26, length: 72, format: 'verifiedLength', screeningSlots: [] },
        ] },
      ],
    } as never
    throw new Error(`Unexpected request ${path}`)
  })
  render(<MemoryRouter><TerminalFamilyReview setId="fixture" initialFamily="mtr20-desk" dataKey="revision" recordDirty={recordDirty} /></MemoryRouter>)
}

it('distinguishes ambiguous capacity, incomplete pairs, and recognized candidates', async () => {
  setup(false)
  const table = await screen.findByRole('region', { name: 'Family table requirements' })
  expect(screen.getByText(/Shared Desk checklist/)).toBeVisible()
  expect(screen.getByText('1 / 3')).toBeVisible()
  const mode = screen.getByRole('note', { name: 'Terminal family and payment mode' })
  expect(within(mode).getByText('Card only (code 1)')).toBeVisible()
  expect(within(mode).getByText('Multi-pay (code 2)')).toBeVisible()
  expect(within(mode).getByText(/payment-mode difference is intentional/)).toBeVisible()
  const screening = within(table).getByRole('row', { name: /Call screening/ })
  expect(within(screening).getByText('Candidate needs review')).toBeVisible()
  expect(within(screening).getByText('Packed editor slots 60 or 85')).toBeVisible()
  expect(within(screening).getByText(screeningNote)).toBeVisible()
  expect(within(screening).getByRole('link', { name: 'MT59' })).toHaveAttribute('href', '/table-sets/fixture?record=59&family=mtr20-desk#config-heading')
  const prompts = within(table).getByRole('row', { name: /Expanded visual prompts/ })
  expect(within(prompts).getByText('Partial record group')).toBeVisible()
  expect(within(prompts).getByText(promptNote)).toBeVisible()
  expect(within(table).getByText('Candidate present')).toBeVisible()
})

it('shows review evidence without navigating away from unsaved changes', async () => {
  setup(true)
  const table = await screen.findByRole('region', { name: 'Family table requirements' })
  expect(within(table).getByText(screeningNote)).toBeVisible()
  expect(within(table).queryByRole('link')).not.toBeInTheDocument()
})
