import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('finds record layouts and distinguishes historical labels from operational messages', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('A local fixture origin is required.')
  const origin = new URL(baseURL).origin
  const token = 'fixture-record-catalogue-reader'
  const definition = { id: 'fixture-legacy-definition', name: 'Historical rate fixture', tableNumber: 10, description: null }
  const catalogue = [
    { number: 10, name: 'Terminal status', kind: 'operational', layouts: [], firmware: [],
      registeredDefinitions: [{ id: definition.id, name: definition.name }] },
    { number: 62, name: 'Numbering plan', kind: 'native_editor', layouts: [{ number:62,length:353,profile:'Later packed profile',draftTemplateAvailable:false,explicitAuthoringAvailable:true }],
      firmware: [],registeredDefinitions: [] },
    { number: 92, name: 'Call screening', kind: 'native_editor',
      layouts: [{ number: 92, length: 3401, profile: 'MTR 1.9 packed', draftTemplateAvailable: true },
        { number: 92, length: 3061, profile: 'Later packed profile', draftTemplateAvailable: true }],
      firmware: [{ profileId: 'reference-older', label: 'Older reference profile', length: 3401, terminalAcknowledged: true,
        completeCatalogue: false, observedLengthsOnly: true, editorStatus: 'length-only' },
        { profileId: 'reference-later', label: 'Later reference profile', length: 3061, terminalAcknowledged: false,
          completeCatalogue: true, observedLengthsOnly: false, editorStatus: 'template', editorProfile: 'Later packed profile' }],
      registeredDefinitions: [], behaviorObservation: 'Exact free route observed on the emulator' },
    { number: 94, name: 'Code download schedule', kind: 'scheduler_record', layouts: [], firmware: [], registeredDefinitions: [] },
  ]
  await page.route('**/*', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Catalogue reader', role: 'Operator',
        permissions: { overview: 1, 'table-definitions': 1 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/tables/reference-records') return route.fulfill({ json: catalogue })
    if (url.pathname === '/api/tables/definitions') return route.fulfill({ json: [definition] })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto('/table-definitions')
  const reference = page.getByRole('region', { name: 'Reference records' })
  const status = reference.getByRole('row').filter({ hasText: 'MT10' })
  await expect(status).toContainText('Terminal status')
  await expect(status).toContainText('Not an install record')
  await expect(status).toContainText('Historical rate fixture')
  const registered = page.getByRole('region', { name: 'Registered table definitions' })
  await expect(registered.getByRole('row').filter({ hasText: 'Historical rate fixture' })).toContainText('Terminal status')
  await expect(page.getByRole('button', { name: 'Create definition' })).toHaveCount(0)
  await page.getByLabel('Find a record').fill('call screening')
  await expect(reference.getByRole('row')).toHaveCount(2)
  const screening = reference.getByRole('row').filter({ hasText: 'Call screening' })
  await screening.getByText('2 layouts').click()
  await expect(screening).toContainText('3,401 bytes')
  await expect(screening).toContainText('3,061 bytes')
  await screening.getByText('2 profiles; 1 acknowledged').click()
  await expect(screening).toContainText('emulator MT14 observed')
  await expect(screening).toContainText('acknowledgement not recorded')
  await expect(screening).toContainText('Exact free route observed on the emulator')
  await page.getByLabel('Target profile').selectOption('reference-older')
  const coverage = page.getByRole('region', { name: 'Selected profile coverage' })
  await expect(coverage).toContainText('Recorded message types: 1; matching native editors: 0; authorable: 0; emulator acknowledgements: 1')
  await expect(coverage).toContainText('Incomplete catalogue')
  await expect(screening).toContainText('Observed length only; editor compatibility unverified')
  await page.getByLabel('Target profile').selectOption('reference-later')
  await expect(coverage).toContainText('Recorded message types: 1; matching native editors: 1; authorable: 1; emulator acknowledgements: 0')
  await expect(screening).toContainText('Matching native editor with draft template')
  await page.getByLabel('Target profile').selectOption('all')
  await page.getByLabel('Find a record').fill('MT62')
  const numbering = reference.getByRole('row').filter({hasText:'Numbering plan'})
  await numbering.getByText('1 layout').click()
  await expect(numbering).toContainText('explicit authoring (no default template)')
  await page.getByLabel('Find a record').fill('')
  await page.getByLabel('Record category').selectOption('scheduler_record')
  await expect(reference.getByRole('row')).toHaveCount(2)
  await expect(reference.getByText('Separate scheduling workflow')).toBeVisible()
})
