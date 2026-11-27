import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('exports configuration JSON and creates only a separately reviewed local draft', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('A local console fixture is required.')
  const origin = new URL(baseURL).origin, token = 'fixture-portable-operator'
  const base = '00000000-0000-4000-8000-000000009801', draft = '00000000-0000-4000-8000-000000009802'
  const packageData = { schema: 'coinline-terminal-configuration-v1', profileId: 'mtr19-multipay-200',
    records: [{ number: 31, payloadBase64: 'fixture', sha256: 'a'.repeat(64), sortOrder: 31, dependsOnNumber: null }], omittedNumbers: [21, 93] }
  const writes: unknown[] = []
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login') return route.fulfill({ json: { token, displayName: 'Fixture operator', role: 'Operator',
      permissions: { overview: 1, 'table-sets': 5, 'table-records': 7 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    if (url.pathname === `/api/tables/sets/${base}` || url.pathname === `/api/tables/sets/${draft}`) {
      const isDraft = url.pathname.endsWith(draft)
      return route.fulfill({ json: { id: isDraft ? draft : base, name: isDraft ? 'Imported fixture' : 'Local fixture base',
        status: 0, revision: 1, customerId: null, isDefault: false, publishedAtUtc: null, publishGeneration: 0,
        optionalRecordRemovals: [], targetFirmwareVersionId: 'firmware-fixture', targetFirmware: 'Fixture target',
        tableProfile: 'MTR 1.9 packed', tableProfileEvidence: { recordCount: 10, terminalAcknowledgedCount: 10, completeCatalogue: false },
        firmwareFindings: [], versions: [] } })
    }
    const endpoint = `/api/tables/sets/${base}/portable-package`
    if (url.pathname === endpoint && request.method() === 'GET') return route.fulfill({ json: packageData })
    if (request.method() !== 'GET') writes.push({ path: url.pathname, body: request.postDataJSON() })
    if (url.pathname === endpoint + '/preview') return route.fulfill({ json: { previewToken: 'fixture-preview', canCreate: true,
      targetFirmware: 'Fixture target', records: [{ number: 31, action: 'replace', length: 37, retainedLocalFields: true }],
      retainedNumbers: [21], missingOmittedNumbers: [93], findings: [] } })
    if (url.pathname === endpoint + '/import') return route.fulfill({ status: 201, json: { id: draft } })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/table-sets/${base}`)
  const panel = page.getByRole('region', { name: 'Portable configuration packages' })
  await panel.getByRole('button', { name: 'Show configuration packages' }).click()
  const downloadPromise = page.waitForEvent('download')
  await panel.getByRole('button', { name: 'Export configuration JSON' }).click()
  const downloaded = await downloadPromise
  expect(downloaded.suggestedFilename()).toBe('coinline-terminal-configuration.json')
  await panel.getByLabel('Configuration package file').setInputFiles({ name: 'configuration.json', mimeType: 'application/json',
    buffer: Buffer.from(JSON.stringify(packageData)) })
  await panel.getByLabel('New draft name').fill('Imported fixture')
  await panel.getByRole('button', { name: 'Preview configuration import' }).click()
  const preview = panel.getByRole('region', { name: 'Configuration import preview' })
  await expect(preview.getByText('MT31')).toBeVisible()
  await expect(preview.getByRole('cell', { name: 'Retained from local base', exact: true })).toBeVisible()
  await expect(preview.getByText(/Omitted package records missing from this base: MT93/)).toBeVisible()
  await expect(preview.getByRole('button', { name: 'Create imported draft' })).toBeDisabled()
  expect(writes).toHaveLength(1)
  await preview.getByRole('checkbox', { name: /reviewed the included/ }).check()
  await preview.getByRole('button', { name: 'Create imported draft' }).click()
  await expect(page.getByRole('heading', { name: 'Table set: Imported fixture' })).toBeVisible()
  expect(writes).toEqual([
    { path: `/api/tables/sets/${base}/portable-package/preview`, body: { name: 'Imported fixture', package: packageData } },
    { path: `/api/tables/sets/${base}/portable-package/import`, body: { name: 'Imported fixture', package: packageData, previewToken: 'fixture-preview', confirm: true } },
  ])
})
