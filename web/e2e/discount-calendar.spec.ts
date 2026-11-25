import { expect, test } from '@playwright/test'
import { signIn } from './auth'
import { discountCalendarFixture } from '../src/pages/timeDiscountPreview.fixture'

test('previews holiday, overnight and overlapping MT152 periods and saves one schedule field', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin, token = 'fixture-discount-calendar-operator'
  const id = '00000000-0000-4000-8000-000000015201'
  let revision = 1, { sections } = discountCalendarFixture()
  const saves: Record<string, unknown>[] = []
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST') return route.fulfill({ json: { token,
      displayName: 'Workshop operator', role: 'Operator', permissions: { overview: 1, 'table-sets': 1, 'table-records': 3 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === `/api/tables/sets/${id}`) return route.fulfill({ json: {
      id, name: 'Discount calendar draft', status: 0, revision: 1, customerId: null, isDefault: false, publishedAtUtc: null,
      publishGeneration: 0, optionalRecordRemovals: [], targetFirmwareVersionId: null, targetFirmware: null,
      tableProfile: null, firmwareFindings: [], versions: [{ id: 'fixture-version', tableDefinitionId: 'fixture-definition',
        definitionName: 'Time-of-day discounts', tableNumber: 152, tableRevision: revision, sortOrder: 1, validationPassed: true,
        payloadSha256Hex: String(revision).repeat(64), payloadLengthBytes: 95, warnings: [] }],
    } })
    if (url.pathname === `/api/tables/sets/${id}/records/152/fields`) {
      if (request.method() === 'PUT') {
        const body = request.postDataJSON(); saves.push(body)
        sections = sections.map(section => ({ ...section, fields: section.fields.map(field => field.key in body.changes ? { ...field, value: body.changes[field.key] } : field) })); revision++
        return route.fulfill({ status: 204 })
      }
      return route.fulfill({ json: { supported: true, editable: true, profile: 'Later packed profile', length: 95,
        revision, sha256: String(revision).repeat(64), sections } })
    }
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/table-sets/${id}?record=152`)
  const panel = page.locator('details.discount-calendar')
  await panel.getByLabel('Preview date').fill('2026-12-25')
  await panel.getByLabel('Preview time').fill('23:59')
  await expect(panel.getByText('Holidays (holiday 1)', { exact: true })).toBeVisible()
  await expect(panel.getByText('MT93 discount 7', { exact: true })).toBeVisible()
  await panel.getByLabel('Preview date').fill('2026-11-28')
  await panel.getByLabel('Preview time').fill('01:59')
  await expect(panel.getByText('22:00 to 02:00 (overnight)', { exact: true })).toBeVisible()
  await expect(panel.getByText('MT93 discount 3', { exact: true })).toBeVisible()
  await panel.getByLabel('Preview date').fill('2026-11-29')
  await expect(panel.getByText('No discount period matches this date and time.')).toBeVisible()
  await panel.getByLabel('Preview date').fill('2026-11-25')
  await panel.getByLabel('Preview time').fill('11:30')
  await expect(panel.getByText('The selected period does not enable this payment. Period 2 is not a fallback.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Save record' })).toBeDisabled()
  await page.getByLabel('Section or entry').selectOption('period-0')
  await page.getByLabel('Ends at hour').fill('11')
  await expect(panel.getByText('MT93 discount 2', { exact: true })).toBeVisible()
  await expect(panel.getByText(/These periods overlap/)).toHaveCount(0)
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(saves).toHaveLength(1)
  expect(saves[0]).toMatchObject({ revision: 1, sha256: '1'.repeat(64), changes: { 'period-0.endHour': '11' } })
  expect(saves[0].changes).toEqual({ 'period-0.endHour': '11' })
  await page.reload()
  await panel.getByLabel('Preview date').fill('2026-11-25')
  await panel.getByLabel('Preview time').fill('11:30')
  await expect(panel.getByText('MT93 discount 2', { exact: true })).toBeVisible()
  await expect(panel.getByText('The selected MT152 period enables this payment and references a discount row.')).toBeVisible()
})
