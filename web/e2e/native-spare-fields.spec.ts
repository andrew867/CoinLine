import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('edits later screening PDS and one spare byte independently, then reopens the saved entry', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin
  const setId = '00000000-0000-4000-8000-000000000401'
  const endpoint = `/api/tables/sets/${setId}/records/92/fields`
  const token = 'fixture-spare-operator'
  const field = (key: string, label: string, value: string, editor: string, isSpare = false) =>
    ({ key, label, value, editor, isSpare, min: 0, max: 255, maxLength: null, choices: null, help: null })
  let fields = [
    field('rule-0.pattern', 'Number pattern', '1020', 'text'),
    field('rule-0.pds', 'Enable PDS dialing', 'false', 'checkbox'),
    field('rule-0.spare.byte2', 'Spare byte 2', '50', 'number', true),
  ]
  let revision = 1
  let savedChanges: Record<string, string> = {}
  const layout = () => ({ supported: true, editable: true, profile: 'Later packed profile', length: 3061, revision, sha256: 'a'.repeat(64),
    screening: { descriptions: {}, supportsSpeedDial: true }, sections: [{ key: 'rule-0', label: 'Screening rule 1', fields }] })
  const set = () => ({ id: setId, name: 'Workshop screening', status: 0, revision: 1, customerId: null,
    isDefault: false, publishedAtUtc: null, publishGeneration: 0, optionalRecordRemovals: [], targetFirmwareVersionId: null,
    targetFirmware: null, tableProfile: null, firmwareFindings: [], versions: [{ id: 'fixture-version', tableDefinitionId: 'fixture-definition',
      definitionName: 'Call-Screening List', tableNumber: 92, tableRevision: revision, sortOrder: 1, validationPassed: true,
      payloadSha256Hex: 'a'.repeat(64), payloadLengthBytes: 3061, warnings: [] }] })
  await page.route('**/*', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST') {
      return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator',
        permissions: { overview: 1, 'table-sets': 1, 'table-records': 3 } } })
    }
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === `/api/tables/sets/${setId}`) return route.fulfill({ json: set() })
    if (url.pathname === endpoint) {
      if (request.method() === 'PUT') {
        savedChanges = request.postDataJSON().changes
        fields = fields.map(field => field.key in savedChanges ? { ...field, value: savedChanges[field.key] } : field)
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
  await page.goto(`/table-sets/${setId}?record=92`)
  const pds = page.getByLabel('Enable PDS dialing')
  const spare = page.getByLabel('Spare byte 2')
  await expect(pds).toBeVisible()
  await expect(spare).not.toBeVisible()
  await pds.check()
  await page.getByText('Spare fields', { exact: true }).click()
  await expect(spare).toHaveValue('50')
  await spare.fill('99')
  await expect(page.getByText('Spare fields (1 changed)', { exact: true })).toBeVisible()
  await expect(page.getByLabel('Number pattern')).toHaveValue('1020')
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(savedChanges).toEqual({ 'rule-0.pds': 'true', 'rule-0.spare.byte2': '99' })

  await page.reload()
  await expect(pds).toBeChecked()
  await page.getByText('Spare fields', { exact: true }).click()
  await expect(spare).toHaveValue('99')
  await page.getByRole('button', { name: 'Delete entry' }).click()
  await expect(pds).not.toBeVisible()
  await expect(spare).not.toBeVisible()
  await page.getByRole('button', { name: 'Undo screening action' }).click()
  await expect(pds).toBeChecked()
})
