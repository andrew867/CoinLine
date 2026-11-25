import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { apiGet } from '../api/client'
import { SmartcardTypesPage } from './SmartcardTypesPage'

vi.mock('../api/client', () => ({ apiGet: vi.fn() }))
afterEach(() => { cleanup(); vi.resetAllMocks() })

const catalogue = {
  referenceScope: 'Two reference configurations', physicalCardTransactionsQualified: false, referenceApplicationEvidenceOnly: true,
  applications: [
    { key: 'gpm103-disposable', name: 'GPM103 disposable', family: 'gpm103', subtype: 'disposable', transport: 'synchronous_memory',
      enabledInInspectedBuilds: true, implementationEvidence: 'active_handler', hostClassification: 2, qualification: 'payment_unqualified' },
    { key: 'proton-orca', name: 'Proton ORCA', family: 'proton', subtype: 'reloadable', transport: 'asynchronous_processor',
      enabledInInspectedBuilds: false, implementationEvidence: 'application_disabled', hostClassification: 6, qualification: 'payment_unqualified' },
  ],
}

it('distinguishes transport and application evidence from payment qualification', async () => {
  vi.mocked(apiGet).mockImplementation(async path => path === '/api/smartcards/applications' ? catalogue : [])
  render(<SmartcardTypesPage />)
  const matrix = await screen.findByRole('region', { name: 'Smartcard application matrix' })
  const gpm = within(matrix).getByRole('row', { name: /GPM103/ })
  expect(gpm).toHaveTextContent('Enabled')
  expect(gpm).toHaveTextContent('Not qualified')
  const proton = within(matrix).getByRole('row', { name: /Proton/ })
  expect(proton).toHaveTextContent('Disabled')
  expect(proton).toHaveTextContent('Not qualified')
  fireEvent.change(screen.getByLabelText('Contact transport'), { target: { value: 'asynchronous_processor' } })
  expect(gpm).not.toBeInTheDocument()
  expect(proton).toBeVisible()
})

it('preserves registered host classifications if evidence is unavailable', async () => {
  vi.mocked(apiGet).mockImplementation(async path => {
    if (path === '/api/smartcards/applications') throw new Error('Unavailable')
    return [{ id: 'legacy', code: 'SAMPLE', name: 'Legacy row', atrProfile: 0, mapsToCardType: 5, notes: '' }]
  })
  render(<SmartcardTypesPage />)
  expect(await screen.findByRole('alert')).toHaveTextContent('Could not load application catalogue')
  expect(await screen.findByText('Legacy row')).toBeVisible()
})
