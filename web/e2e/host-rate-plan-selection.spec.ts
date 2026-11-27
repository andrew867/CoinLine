import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('sets an explicit customer host plan for inheriting phones without scheduling a download', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('A local console fixture origin is required.')
  const origin = new URL(baseURL).origin
  const customer = '00000000-0000-4000-8000-000000005001', plan = '00000000-0000-4000-8000-000000005002'
  const token = 'fixture-selection-operator'
  const writes: unknown[] = []
  let saved = false
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login') return route.fulfill({ json: { token, role: 'Operator', displayName: 'Fixture operator',
      permissions: { overview: 1, customers: 3, 'rate-plans': 3 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === `/api/operator/customers/${customer}/console`) return route.fulfill({ json: {
      customer: { id: customer, name: 'Fixture customer', code: 'LAB' }, sites: [], terminals: [], tableSets: [], ratePlans: [], cardProducts: [], cardProductsNote: null } })
    if (url.pathname === `/api/rate-plans/selection/customers/${customer}` && request.method() === 'GET')
      return route.fulfill({ json: { selectionId: saved ? 'new-selection' : null, selectedPlanId: saved ? plan : null, revision: saved ? 1 : null,
        inheritedPhones: 2, effective: { customerId: customer, ratePlanId: saved ? plan : null, publishedVersionId: saved ? 'v1' : null,
          name: saved ? 'Regional calling' : null, source: saved ? 'customer' : 'none' },
        options: [{ id: plan, name: 'Regional calling', customerId: null, publishedVersionId: 'v1' }] } })
    if (request.method() !== 'GET') writes.push({ path: url.pathname, method: request.method(), body: request.postDataJSON() })
    if (url.pathname === `/api/rate-plans/selection/customers/${customer}` && request.method() === 'PUT') {
      saved = true; return route.fulfill({ status: 204 })
    }
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/customers/${customer}`)
  const region = page.getByRole('region', { name: 'Host rate plan selection' })
  await expect(region.getByText('2 active phones inherit this default. Explicit phone selections are retained.')).toBeVisible()
  await region.getByLabel('Customer default host rate plan').selectOption(plan)
  expect(writes).toEqual([])
  page.once('dialog', dialog => dialog.accept())
  await region.getByRole('button', { name: 'Save host rate plan selection' }).click()
  await expect(region.getByRole('link', { name: 'Regional calling' })).toHaveAttribute('href', `/rate-plans/${plan}`)
  await expect(region.getByText('Host rate plan selection saved. No terminal table download was requested.')).toBeVisible()
  expect(writes).toEqual([{ path: `/api/rate-plans/selection/customers/${customer}`, method: 'PUT',
    body: { ratePlanId: plan, expectedSelectionId: null, expectedRevision: null, confirm: true } }])
})
