import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('previews bulk screening across configurations, resolves the scope conflict, and opens the replacement draft', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin, token = 'fixture-screening-bulk-operator'
  const source = '00000000-0000-4000-8000-000000920001', draft = '00000000-0000-4000-8000-000000920002'
  const published = '00000000-0000-4000-8000-000000920003', replacement = '00000000-0000-4000-8000-000000920004'
  const endpoint = `/api/tables/sets/${source}/records/92/screening-bulk`
  const pattern = '3335550123', profile = 'MTR 1.9 packed', sha256 = 'a'.repeat(64)
  const submitted: Record<string, unknown>[] = []
  let created = false
  const layout = (id: string) => ({ supported: true, editable: id === replacement, profile, length: 3401, revision: id === replacement ? 4 : 3,
    sha256, screening: { descriptions: { 2: 'Fixture entry' }, supportsSpeedDial: false }, sections: [0, 1].map(index => ({ key: `rule-${index}`,
      label: `Screening rule ${index + 1}`, fields: [{ key: `rule-${index}.pattern`, label: 'Number pattern', value: index === 1 ? pattern : '', editor: 'text',
        min: 0, max: 0, maxLength: 16, choices: null, help: null }] })) })
  const set = (id: string) => ({ id, name: id === source ? 'Source screening' : 'Replacement screening draft', status: id === source ? 1 : 0,
    revision: 1, customerId: null, isDefault: false, publishedAtUtc: null, publishGeneration: id === source ? 1 : 0,
    optionalRecordRemovals: [], targetFirmwareVersionId: null, targetFirmware: null, tableProfile: null, firmwareFindings: [],
    versions: [{ id: 'fixture-version', tableDefinitionId: 'fixture-definition', definitionName: 'Call-Screening List', tableNumber: 92,
      tableRevision: id === source ? 3 : 4, sortOrder: 1, validationPassed: true, payloadSha256Hex: sha256, payloadLengthBytes: 3401, warnings: [] }] })
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST') return route.fulfill({ json: { token, displayName: 'Fixture operator', role: 'Operator',
      permissions: { overview: 1, 'table-sets': 7, 'table-records': 3, 'table-assignments': 1 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    for (const id of [source, ...(created ? [replacement] : [])]) {
      if (url.pathname === `/api/tables/sets/${id}`) return route.fulfill({ json: set(id) })
      if (url.pathname === `/api/tables/sets/${id}/records/92/fields`) return route.fulfill({ json: layout(id) })
    }
    if (url.pathname === endpoint + '/preview') {
      const body = request.postDataJSON(), blocked = body.customerScope === 'all' || body.action === 'delete'
      return route.fulfill({ json: { previewToken: blocked ? 'blocked' : 'ready', canApply: !blocked, source: 'Source screening', sourcePattern: pattern,
        slot: 2, changedCount: 2, blockedCount: blocked ? 1 : 0, updatedDrafts: 1, replacementDrafts: 1,
        targets: [{ setId: draft, name: 'Vacant draft', status: 'update-draft', published: false, inUse: false, assignedPhones: 0 },
          { setId: published, name: 'Published target', status: 'replacement-draft', published: true, inUse: true, assignedPhones: 3 },
          ...(blocked ? [{ setId: 'occupied', name: 'Blocking configuration', status: 'blocked', published: false, inUse: true,
            reason: body.action === 'delete' ? 'This configuration is assigned.' : 'The destination slot is occupied.' }] : [])] } })
    }
    if (url.pathname === endpoint && request.method() === 'POST') {
      submitted.push(request.postDataJSON()); created = true
      return route.fulfill({ json: { affectedCount: 2, configurations: [{ sourceSetId: draft, setId: draft, name: 'Vacant draft', revision: 4, replacement: false },
        { sourceSetId: published, setId: replacement, name: 'Replacement screening draft', revision: 4, replacement: true }] } })
    }
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/table-sets/${source}?record=92`)
  await page.getByLabel('Section or entry').selectOption('rule-1')
  await expect(page.getByLabel('Number pattern')).toBeDisabled()
  await page.getByText('Change screening slot 2 across configurations').click()
  await page.getByLabel('Bulk screening operation').selectOption('delete')
  await page.getByRole('button', { name: 'Preview bulk screening change' }).click()
  await expect(page.getByText('This configuration is assigned.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Apply bulk screening change' })).toHaveCount(0)
  await page.getByLabel('Bulk screening operation').selectOption('add')
  await page.getByLabel('Ownership scope').selectOption('all')
  await page.getByRole('button', { name: 'Preview bulk screening change' }).click()
  await expect(page.getByText('The destination slot is occupied.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Apply bulk screening change' })).toHaveCount(0)
  await page.getByLabel('Ownership scope').selectOption('source')
  await page.getByRole('button', { name: 'Preview bulk screening change' }).click()
  const preview = page.getByRole('region', { name: 'Bulk screening preview' })
  await expect(preview.getByText('1 existing drafts to update; 1 replacement drafts to prepare; 0 blocked.')).toBeVisible()
  await expect(preview.getByText('Published target', { exact: true })).toBeVisible()
  const apply = page.getByRole('button', { name: 'Apply bulk screening change' })
  await expect(apply).toBeDisabled()
  await page.getByLabel('I reviewed all 2 changes.').check()
  await apply.click()
  const result = page.getByRole('region', { name: 'Bulk screening result' })
  await expect(result.getByText(/No phones or downloads were changed/)).toBeVisible()
  expect(submitted).toEqual([{ revision: 3, sha256, profile, slot: 2, action: 'add', includePublished: true, customerScope: 'source', previewToken: 'ready' }])
  await result.getByRole('link', { name: 'Replacement screening draft' }).click()
  await expect(page.getByRole('heading', { name: 'Table set: Replacement screening draft' })).toBeVisible()
  await page.getByLabel('Section or entry').selectOption('rule-1')
  await expect(page.getByLabel('Number pattern')).toHaveValue(pattern)
  await expect(page.getByLabel('Number pattern')).toBeEnabled()
})
