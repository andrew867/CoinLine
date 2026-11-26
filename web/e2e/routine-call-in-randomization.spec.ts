import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('reviews individual routine times before saving an exact MT35 rollout', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('A local fixture origin is required.')
  const origin = new URL(baseURL).origin
  const token = 'fixture-routine-operator'
  const setId = '00000000-0000-4000-8000-000000930001', endpoint = `/api/tables/sets/${setId}`
  const phones = [{ id: 'fixture-phone-a', displayName: 'Workshop phone A' }, { id: 'fixture-phone-b', displayName: 'Workshop phone B' }]
  const reviewed = { mode: 'window', windowStart: '01:00:00', windowEnd: '04:00:00', seed: '11111111-2222-3333-4444-555555555555' }
  const requests: Record<string, unknown>[] = [], unexpectedMutations: string[] = []
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST') return route.fulfill({ json: {
      token, role: 'Operator', displayName: 'Fixture operator',
      permissions: { overview: 1, 'table-sets': 1, 'table-records': 2, 'table-assignments': 1 },
    } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === endpoint && request.method() === 'GET') return route.fulfill({ json: {
      id: setId, name: 'Routine fixture configuration', status: 1, revision: 3, customerId: null, isDefault: false,
      optionalRecordRemovals: [], firmwareFindings: [], versions: [{ id: 'version-35', tableDefinitionId: 'definition-35',
        definitionName: 'Time and call-in settings', tableNumber: 35, tableRevision: 2, sortOrder: 1,
        validationPassed: true, payloadSha256Hex: 'a'.repeat(64), payloadLengthBytes: 21, configurationId: 'CALL01', warnings: [] }],
    } })
    if (url.pathname === '/api/tables/scope-options') return route.fulfill({ json: url.searchParams.get('targetType') === 'terminal' ? phones
      : url.searchParams.get('targetType') === 'group' ? [{ id: 'fixture-group', name: 'Workshop group' }] : [] })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    if (url.pathname === endpoint + '/record-updates/runs') return route.fulfill({ json: [] })
    if (url.pathname === endpoint + '/record-updates/preview' && request.method() === 'POST') {
      requests.push(request.postDataJSON())
      return route.fulfill({ json: { previewToken: 'reviewed-routine-times', changeCount: 2, setCount: 2, pendingCount: 0,
        rolloutId: null, callInRandomization: reviewed, targets: phones.map((phone, i) => ({
          ...phone, baseSetName: 'Existing set', callInTime: ['01:25:17', '03:40:05'][i], reason: null,
          changes: [{ tableNumber: 35, action: 'replace', required: false }],
        })) } })
    }
    if (url.pathname === endpoint + '/record-updates' && request.method() === 'POST') {
      requests.push(request.postDataJSON())
      return route.fulfill({ json: { changed: 2, sets: phones.map((phone, i) => ({
        id: `individual-set-${i}`, name: `Routine configuration ${i + 1}`, phoneId: phone.id, phoneDisplayName: phone.displayName,
      })) } })
    }
    if (request.method() !== 'GET') unexpectedMutations.push(url.pathname)
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/table-sets/${setId}?randomizePhone=${phones[0].id}#table-assignment-heading`)
  await expect(page.getByRole('heading', { name: 'Table set: Routine fixture configuration' })).toBeVisible()
  await expect(page.getByRole('combobox', { name: 'Phone', exact: true })).toHaveValue(phones[0].id)
  await expect(page.getByLabel('Assignment action')).toHaveValue('records')
  await expect(page.getByLabel("Randomize each phone's routine MT35 call-in time")).toBeChecked()
  await page.getByLabel('Assign by').selectOption('group')
  await page.getByRole('combobox', { name: 'Phone group', exact: true }).selectOption('fixture-group')
  await page.getByLabel('Randomization range').selectOption('window')
  await page.getByLabel('Earliest call-in (phone clock)').fill('01:00:00')
  await page.getByLabel('Latest call-in (phone clock)').fill('04:00:00')
  await page.getByRole('button', { name: 'Preview record update' }).click()
  const review = page.getByRole('region', { name: 'Record update review' })
  await expect(review.getByText('Workshop phone A')).toBeVisible()
  await expect(review.getByText('01:25:17')).toBeVisible()
  await expect(review.getByText('Workshop phone B')).toBeVisible()
  await expect(review.getByText('03:40:05')).toBeVisible()
  expect(requests).toHaveLength(1)
  await page.getByRole('button', { name: 'Update 2 phones' }).click()
  await expect(page.getByRole('link', { name: 'Routine configuration 1 · Workshop phone A' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Routine configuration 2 · Workshop phone B' })).toBeVisible()
  expect(requests).toHaveLength(2)
  expect(requests[0]).toMatchObject({ targetType: 'group', targetId: 'fixture-group', copyRecords: [35],
    callInRandomization: { mode: 'window', windowStart: '01:00:00', windowEnd: '04:00:00' } })
  expect(requests[1]).toMatchObject({ previewToken: 'reviewed-routine-times', copyRecords: [35], callInRandomization: reviewed })
  expect(unexpectedMutations).toEqual([])
})
