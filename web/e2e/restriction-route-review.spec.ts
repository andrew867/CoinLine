import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('reviews marked restricted routes without changing companion records', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('A local fixture origin is required.')
  const origin = new URL(baseURL).origin
  const token = 'fixture-restriction-operator'
  const setId = '00000000-0000-4000-8000-000000000852'
  const endpoint = `/api/tables/sets/${setId}/records/58/fields`
  let level = '4'
  let revision = 1
  let savedChanges: Record<string, string> | null = null
  const numberField = (key: string, label: string, value: string, max: number) =>
    ({ key, label, value, editor: 'number', isSpare: false, min: 0, max, maxLength: null, choices: null, help: null })
  const layout = () => ({
    supported: true, editable: true, profile: 'Later packed profile', length: 26, revision, sha256: 'a'.repeat(64),
    sections: [{ key: 'restriction', label: 'Daily service restriction', fields: [
      { ...numberField('restriction', 'Restriction level', level, 4), editor: 'select',
        choices: [{ value: 0, label: 'None' }, { value: 4, label: 'Configurable restricted access' }] },
      numberField('startHour', 'Start hour (phone local time)', '20', 23), numberField('startMinute', 'Start minute', '0', 59),
      numberField('durationHours', 'Duration hours', '6', 24), numberField('durationMinutes', 'Duration minutes', '0', 59),
    ] }],
    restrictedRoutes: [
      { number: 92, availability: 'included', routes: [
        { slot: 1, destination: '33355501??', classification: 3, classificationLabel: 'Local', dialable: true,
          status: 'configured_dialable', trailingWildcards: 2, translated: false, freeCall: false },
        { slot: 2, destination: '3335550102', classification: 11, classificationLabel: 'Denied', dialable: false,
          status: 'denied', trailingWildcards: 0, translated: false },
        { slot: 3, destination: '', classification: 3, classificationLabel: 'Local', dialable: false,
          status: 'blank', trailingWildcards: 0, translated: false },
      ] },
      { number: 55, availability: 'missing', routes: [] },
      { number: 133, availability: 'included', routes: [
        { slot: 1, positionLabel: 'Screen 21, panel 2, key 1', destination: '3335550102', classification: 3,
          classificationLabel: 'Local', dialable: true, status: 'configured_dialable', freeCall: true },
        { slot: 2, positionLabel: 'Screen 21, panel 2, key 2', destination: '', classification: 3,
          classificationLabel: 'Local', dialable: false, status: 'navigation_only', freeCall: false },
      ] },
    ],
  })
  const set = () => ({ id: setId, name: 'Workshop restricted service', status: 0, revision: 1, customerId: null, isDefault: false,
    publishedAtUtc: null, publishGeneration: 0, optionalRecordRemovals: [], targetFirmwareVersionId: null,
    targetFirmware: null, tableProfile: null, firmwareFindings: [], versions: [{ id: 'fixture-version', tableDefinitionId: 'fixture-definition',
      definitionName: 'Service restrictions', tableNumber: 58, tableRevision: revision,
      sortOrder: 1, validationPassed: true, payloadSha256Hex: 'a'.repeat(64), payloadLengthBytes: 26, warnings: [] }] })
  await page.route('**/*', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator',
        permissions: { overview: 1, 'table-sets': 1, 'table-records': 3 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === `/api/tables/sets/${setId}`) return route.fulfill({ json: set() })
    if (url.pathname === endpoint) {
      if (request.method() === 'PUT') {
        const changes = request.postDataJSON().changes as Record<string, string>
        savedChanges = changes
        level = changes.restriction ?? level
        revision++
        return route.fulfill({ status: 204 })
      }
      return route.fulfill({ json: layout() })
    }
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/table-sets/${setId}?record=58`)
  const review = page.getByRole('region', { name: 'Permitted routes review' })
  await expect(review.getByText('2 configured dialable candidates in the included records.')).toBeVisible()
  await expect(review.getByRole('row').filter({ hasText: 'Entry 1' })).toContainText('33355501??')
  await expect(review.getByRole('row').filter({ hasText: 'Entry 1' })).toContainText('2 trailing wildcard digits')
  await expect(review.getByRole('row').filter({ hasText: 'Entry 1' })).toContainText('Review marked paid route')
  await expect(review.getByRole('row').filter({ hasText: 'Screen 21, panel 2, key 1' })).toContainText('Free')
  await expect(review.getByRole('row').filter({ hasText: 'Screen 21, panel 2, key 2' })).toContainText('Navigation only; no dialed destination')
  await expect(review.getByRole('row').filter({ hasText: 'Entry 2' })).toContainText('Denied call classification')
  await expect(review.getByRole('row').filter({ hasText: 'Entry 3' })).toContainText('Blank destination')
  await expect(review.getByText('Not included in this set. The terminal may retain an existing record.')).toBeVisible()
  await page.getByLabel('Restriction level').selectOption('0')
  await expect(review).toHaveCount(0)
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(savedChanges).toEqual({ restriction: '0' })
  await page.reload()
  await expect(page.getByLabel('Restriction level')).toHaveValue('0')
  await expect(review).toHaveCount(0)
  await page.getByLabel('Restriction level').selectOption('4')
  await expect(review.getByText('2 configured dialable candidates in the included records.')).toBeVisible()
})
