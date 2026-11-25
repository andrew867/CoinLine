import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('previews and confirms separate phone-specific access assignments only by explicit choice', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin
  const source = '00000000-0000-4000-8000-000000001201'
  const group = '00000000-0000-4000-8000-000000001202'
  const token = 'fixture-access-operator'
  const phones = [
    { id: 'fixture-phone-a', displayName: 'Fixture phone A', terminalIdHex: '0000000002', baseSetName: 'Existing configuration' },
    { id: 'fixture-phone-b', displayName: 'Fixture phone B', terminalIdHex: '0000000003', baseSetName: 'Existing configuration' },
  ]
  const requests: Record<string, unknown>[] = []
  await page.route('**/*', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator',
        permissions: { overview: 1, 'table-sets': 1, 'table-records': 3, 'table-assignments': 3 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === `/api/tables/sets/${source}`) return route.fulfill({ json: {
      id: source, name: 'Shared manager access settings', status: 1, revision: 2, customerId: null,
      isDefault: false, publishedAtUtc: '2026-01-01T00:00:00Z', publishGeneration: 1, optionalRecordRemovals: [], targetFirmwareVersionId: null,
      targetFirmware: null, tableProfile: null, firmwareFindings: [], versions: [
        { id: 'fixture-version-21', tableDefinitionId: 'fixture-definition-21', definitionName: 'Access settings', tableNumber: 21,
          tableRevision: 1, sortOrder: 1, validationPassed: true, payloadSha256Hex: 'a'.repeat(64), payloadLengthBytes: 48, warnings: [] },
        { id: 'fixture-version-50', tableDefinitionId: 'fixture-definition-50', definitionName: 'Coin settings', tableNumber: 50,
          tableRevision: 1, sortOrder: 2, validationPassed: true, payloadSha256Hex: 'b'.repeat(64), payloadLengthBytes: 105, warnings: [] },
      ],
    } })
    if (url.pathname === '/api/tables/scope-options') return route.fulfill({ json: url.searchParams.get('targetType') === 'group'
      ? [{ id: group, name: 'Access fixture group' }] : [] })
    const endpoint = `/api/tables/sets/${source}/record-updates`
    if (url.pathname === endpoint + '/runs') return route.fulfill({ json: [] })
    if (url.pathname === endpoint + '/preview' && request.method() === 'POST') {
      const body = request.postDataJSON()
      requests.push(body)
      const personalized = body.personalizeAccessIdentity === true
      return route.fulfill({ json: { previewToken: personalized ? 'phone-specific-preview' : 'plain-preview',
        changeCount: personalized ? 2 : 0, setCount: personalized ? 2 : 0, pendingCount: 0,
        targets: phones.map(phone => ({ ...phone, reason: personalized ? null : 'This set carries another phone’s wire ID.',
          changes: personalized ? [{ tableNumber: 21, action: 'replace', required: false, personalized: true }] : [] })),
      } })
    }
    if (url.pathname === endpoint && request.method() === 'POST') {
      requests.push(request.postDataJSON())
      return route.fulfill({ json: { changed: 2, sets: [
        { id: 'fixture-set-a', name: 'Access configuration', phoneDisplayName: 'Fixture phone A' },
        { id: 'fixture-set-b', name: 'Access configuration', phoneDisplayName: 'Fixture phone B' },
      ] } })
    }
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    if (url.pathname.endsWith('/fields')) return route.fulfill({ json: { supported: false, reason: 'Fixture read-only record.' } })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/table-sets/${source}?record=21`)
  await page.getByLabel('Assignment action').selectOption('records')
  await page.getByLabel('Assign by').selectOption('group')
  await page.getByRole('combobox', { name: 'Phone group', exact: true }).selectOption(group)
  await page.getByRole('combobox', { name: 'MT21 · Access settings', exact: true }).selectOption('copy')
  await expect(page.getByLabel(/Use each phone's registered wire ID/)).not.toBeChecked()
  await page.getByRole('button', { name: 'Preview record update' }).click()
  await expect(page.getByText(/another phone’s wire ID/)).toHaveCount(2)
  expect(requests[0]).not.toHaveProperty('personalizeAccessIdentity')
  await page.getByLabel(/Use each phone's registered wire ID/).check()
  await expect(page.getByText(/another phone’s wire ID/)).toHaveCount(0)
  await page.getByRole('button', { name: 'Preview record update' }).click()
  await expect(page.getByText('2 phone assignments will change across 2 resulting sets.')).toBeVisible()
  await expect(page.getByText('Replace MT21 (registered phone ID)')).toHaveCount(2)
  expect(requests[1]).toMatchObject({ copyRecords: [21], removeRecords: [], personalizeAccessIdentity: true })
  await page.getByRole('button', { name: 'Update 2 phones' }).click()
  await expect(page.getByRole('link', { name: 'Access configuration · Fixture phone A' })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Access configuration · Fixture phone B' })).toBeVisible()
  expect(requests[2]).toMatchObject({ copyRecords: [21], personalizeAccessIdentity: true, previewToken: 'phone-specific-preview' })
})
