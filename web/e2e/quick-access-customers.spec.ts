import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('creates a customer and saves only its reference on a five-key MT55 configuration', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin
  const endpoint = '/api/quick-access-customers'
  const setId = '00000000-0000-4000-8000-000000000201'
  const customerId = '00000000-0000-4000-8000-000000000202'
  const token = 'fixture-key-operator'
  let customer: Record<string, unknown> | null = null
  let value = '305419896'
  let revision = 1
  let savedChanges: unknown
  const field = (key: string, label: string, stored: string, editor: string) =>
    ({ key, label, value: stored, editor, min: 0, max: 4294967295, maxLength: null, choices: null, help: null })
  const layout = () => ({ supported: true, editable: true, profile: 'Fixture', length: 571, revision, sha256: 'a'.repeat(64),
    sections: Array.from({ length: 10 }, (_, index) => ({ key: `key-${index}`, label: `Key ${index + 1}`, fields: [
      field(`key-${index}.number`, 'Destination', '12', 'text'),
      field(`key-${index}.customerReference`, 'Customer reference', index === 5 ? value : '0', 'number'),
    ] })) })
  const set = () => ({ id: setId, name: 'Workshop keys', status: 0, revision: 1, customerId: null, isDefault: false,
    publishedAtUtc: null, publishGeneration: 0, optionalRecordRemovals: [], targetFirmwareVersionId: null, targetFirmware: null,
    tableProfile: null, firmwareFindings: [], versions: [{ id: 'fixture-version', tableDefinitionId: 'fixture-definition',
      definitionName: 'Quick Access Keys', tableNumber: 55, tableRevision: revision, sortOrder: 1, validationPassed: true,
      payloadSha256Hex: 'a'.repeat(64), payloadLengthBytes: 571, warnings: [] }] })
  await page.route('**/*', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST') {
      return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator',
        permissions: { overview: 1, 'table-sets': 1, 'table-records': 3, 'quick-access-customers': 7 } } })
    }
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === endpoint && request.method() === 'POST') {
      customer = { ...request.postDataJSON(), id: customerId, revision: 1, archived: false }
      return route.fulfill({ status: 201, json: customer })
    }
    if (url.pathname === endpoint + '/search' || url.pathname === endpoint + '/lookup') {
      const rows = customer ? [url.pathname.endsWith('/lookup') ? { reference: customer.reference, name: customer.name }
        : { id: customerId, reference: customer.reference, name: customer.name, revision: 1, archived: false }] : []
      return route.fulfill({ json: { rows, total: rows.length, page: 1, pageSize: 50 } })
    }
    if (url.pathname === endpoint + '/' + customerId && customer) return route.fulfill({ json: customer })
    if (url.pathname === `/api/tables/sets/${setId}`) return route.fulfill({ json: set() })
    if (url.pathname === `/api/tables/sets/${setId}/records/55/fields`) {
      if (request.method() === 'PUT') {
        savedChanges = request.postDataJSON().changes
        value = request.postDataJSON().changes['key-5.customerReference']
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
  await page.getByRole('link', { name: 'Quick Access Customers' }).click()
  await expect(page.getByRole('heading', { name: 'Quick Access Key Customer List' })).toBeVisible()
  await page.getByLabel('Customer reference', { exact: true }).fill('000123')
  await page.getByLabel('Customer name', { exact: true }).fill('Workshop customer')
  await page.getByLabel('Billing comments').fill('Host-only fixture')
  await page.getByRole('button', { name: 'Save customer' }).click()
  await expect(page.getByText('Customer directory entry saved.')).toBeVisible()
  await expect(page.getByLabel('Customer reference', { exact: true })).toHaveValue('000123')

  await page.goto(`/table-sets/${setId}?record=55`)
  await page.getByLabel('Panel layout').selectOption('5')
  await page.getByRole('button', { name: /^Key 6 / }).click()
  const reference = page.getByLabel('Customer reference', { exact: true })
  await expect(reference).toHaveValue('305419896')
  await expect(page.getByText(/outside the six-digit directory/)).toBeVisible()
  await page.getByRole('button', { name: 'Look up customer' }).click()
  await expect(page.getByText('Workshop customer', { exact: true })).toBeVisible()
  await expect(page.getByText('Host-only fixture', { exact: true })).toHaveCount(0)
  await page.getByRole('button', { name: 'Use 000123' }).click()
  await expect(reference).toHaveValue('123')
  await expect(page.getByLabel('Destination', { exact: true })).toHaveValue('12')
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(savedChanges).toEqual({ 'key-5.customerReference': '123' })
  await page.reload()
  await page.getByLabel('Panel layout').selectOption('5')
  await page.getByRole('button', { name: /^Key 6 / }).click()
  await expect(reference).toHaveValue('123')
  await expect(page.getByLabel('Destination', { exact: true })).toHaveValue('12')
})
