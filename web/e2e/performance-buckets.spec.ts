import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('previews configured MT37 histogram boundaries, saves selected MT32 timing, and reopens it', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin, token = 'fixture-performance-operator'
  const id = '00000000-0000-4000-8000-000000003201'
  let revision = 1, start = '100'
  const saves: Record<string, unknown>[] = []
  const field = (key: string, label: string, value: string) => ({ key, label, value, editor: 'number', min: 0,
    max: 32767, maxLength: null, choices: null, help: null })
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator',
        permissions: { overview: 1, 'table-sets': 1, 'table-records': 3 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === `/api/tables/sets/${id}`) return route.fulfill({ json: {
      id, name: 'Communications statistics draft', status: 0, revision: 1, customerId: null, isDefault: false, publishedAtUtc: null,
      publishGeneration: 0, optionalRecordRemovals: [], targetFirmwareVersionId: null, targetFirmware: null,
      tableProfile: null, firmwareFindings: [], versions: [{ id: 'fixture-version', tableDefinitionId: 'fixture-definition',
        definitionName: 'Communications statistics settings', tableNumber: 32, tableRevision: revision, sortOrder: 1, validationPassed: true,
        payloadSha256Hex: String(revision).repeat(64), payloadLengthBytes: 33, warnings: [] }],
    } })
    if (url.pathname === `/api/tables/sets/${id}/records/32/fields`) {
      if (request.method() === 'PUT') {
        const body = request.postDataJSON(); saves.push(body); start = body.changes['interval-0.start'] ?? start; revision++
        return route.fulfill({ status: 204 })
      }
      return route.fulfill({ json: { supported: true, editable: true, profile: 'MTR 1.9 packed', length: 33,
        revision, sha256: String(revision).repeat(64), sections: [{ key: 'collection', label: 'Performance reporting', fields: [field('days', 'Collection interval (days)', '1')] },
          ...[[0, 'Access to dial complete', start, '50'], [1, 'Dial complete to carrier', '1000', '0'],
            [3, 'Carrier to first packet', '5', '13107'], [4, 'User waiting time', '-1', '100']].map(([index, label, first, interval]) => ({
            key: `interval-${index}`, label, fields: [field(`interval-${index}.start`, 'Start (10 ms ticks)', String(first)),
              field(`interval-${index}.duration`, 'Interval (10 ms ticks)', String(interval))],
          }))] } })
    }
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/table-sets/${id}?record=32`)
  await page.getByText('Performance histogram bucket ranges (MT37)', { exact: true }).click()
  const range = page.getByRole('region', { name: 'Access to dial complete', exact: true })
  await expect(range.getByText('3.50 s or more')).toBeVisible()
  await expect(page.getByRole('region', { name: 'Dial complete to carrier', exact: true }).getByText('Empty range')).toHaveCount(5)
  await expect(page.getByRole('region', { name: 'Carrier to first packet', exact: true }).getByRole('status')).toContainText('wraps')
  await expect(page.getByRole('region', { name: 'User waiting time', exact: true }).getByRole('status')).toContainText('cannot be calculated')
  await page.getByLabel('Section or entry').selectOption('interval-0')
  await page.getByLabel('Start (10 ms ticks)').fill('150')
  await expect(range.getByText('4.00 s or more')).toBeVisible()
  await page.getByLabel('Section or entry').selectOption('collection')
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(saves).toHaveLength(1)
  expect(saves[0]).toMatchObject({ revision: 1, sha256: '1'.repeat(64), changes: { 'interval-0.start': '150' } })
  expect(saves[0].changes).toEqual({ 'interval-0.start': '150' })
  await page.reload()
  await page.getByText('Performance histogram bucket ranges (MT37)', { exact: true }).click()
  await expect(range.getByText('4.00 s or more')).toBeVisible()
  await expect(page.getByRole('region', { name: 'Dial complete to carrier', exact: true }).getByText('Empty range')).toHaveCount(5)
})
