import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('imports a regional rate package as a separate draft and exports its selected version', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('A local console fixture origin is required.')
  const origin = new URL(baseURL).origin
  const plan = '00000000-0000-4000-8000-000000004901', version = '00000000-0000-4000-8000-000000004902'
  const token = 'fixture-rating-operator'
  const pack = { formatVersion: 1, name: 'Fixture regional tariffs', mode: 1, rules: [
    { priority: 100, matchKind: 1, pattern: '^709[2-9][0-9]{6}$', outcome: 0, pricingMode: 1, initialChargeUsd: .25, ratePerMinuteUsd: 0 },
    { priority: 200, matchKind: 2, pattern: '411', outcome: 0, pricingMode: 1, initialChargeUsd: 1, ratePerMinuteUsd: 0, overrideFreeClass: true },
  ], tariffs: [], destinationPrefixes: [], timeBands: [] }
  const writes: unknown[] = []
  let imported = false
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login') return route.fulfill({ json: { token, displayName: 'Fixture operator', role: 'Operator',
      permissions: { overview: 1, 'rate-plans': 5 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/rate-plans' && request.method() === 'GET') return route.fulfill({ json: imported ?
      [{ id: plan, name: pack.name, mode: 1, customerId: null, publishedVersionId: null }] : [] })
    if (url.pathname === '/api/rate-plans/packages/regional-starter') return route.fulfill({ json: pack })
    if (url.pathname === '/api/rate-plans/packages/preview') return route.fulfill({ json: { name: pack.name,
      rules: 2, tariffs: 0, destinationPrefixes: 0, timeBands: 0, explicitPaidServiceOverrides: 1, notice: 'Independent draft only.' } })
    if (request.method() !== 'GET') writes.push({ path: url.pathname, body: request.postDataJSON() })
    if (url.pathname === '/api/rate-plans/packages/import' && request.method() === 'POST') {
      imported = true; return route.fulfill({ status: 201, json: { id: plan, versionId: version, status: 0 } })
    }
    if (url.pathname === `/api/rate-plans/${plan}`) return route.fulfill({ json: { id: plan, name: pack.name, mode: 1, customerId: null,
      publishedVersionId: null, versions: [{ id: version, versionNumber: 1, status: 0, publishedAtUtc: null }] } })
    if (url.pathname === `/api/rate-plans/${plan}/versions/${version}`) return route.fulfill({ json: { id: version, versionNumber: 1, status: 0,
      rules: pack.rules.map((rule, index) => ({ ...rule, id: String(index), expression: '{}' })), tariffs: [], destinationPrefixes: [], timeBands: [] } })
    if (url.pathname === `/api/rate-plans/packages/${plan}/${version}`) return route.fulfill({ json: pack })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto('/rate-plans')
  await page.getByRole('button', { name: /Load Newfoundland/ }).click()
  await expect(page.getByText('709 ten-digit: $0.25 for the whole call.')).toBeVisible()
  await expect(page.getByText('US 1+10-digit: $1.50 first minute, $0.50 each additional minute.')).toBeVisible()
  await page.getByRole('button', { name: 'Preview rate plan package' }).click()
  await expect(page.getByText('Independent draft only.')).toBeVisible()
  expect(writes).toEqual([])
  page.once('dialog', dialog => dialog.accept())
  await page.getByRole('button', { name: 'Create imported rate plan draft' }).click()
  await page.getByRole('link', { name: 'Open imported rate plan' }).click()
  await page.getByRole('button', { name: 'v1' }).click()
  await expect(page.getByText('^709[2-9][0-9]{6}$', { exact: true })).toBeVisible()
  const download = page.waitForEvent('download')
  await page.getByRole('button', { name: 'Export selected version as JSON' }).click()
  expect((await download).suggestedFilename()).toBe('coinline-rate-plan.json')
  expect(writes).toEqual([{ path: '/api/rate-plans/packages/import', body: { package: pack, name: pack.name } }])
})
