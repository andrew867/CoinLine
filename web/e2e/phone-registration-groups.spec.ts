import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('registers a new phone and its chosen customer and shared memberships in one request', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('A local fixture origin is required.')
  const origin = new URL(baseURL).origin
  const token = 'fixture-registration-token'
  const requests: unknown[] = []
  const sites = [{ id: 'site-east', name: 'East fixture site', code: 'EAST', customerId: 'customer-east' },
    { id: 'site-west', name: 'West fixture site', code: 'WEST', customerId: 'customer-west' }]
  const groups = [{ id: 'group-east', name: 'East group', customerId: 'customer-east' },
    { id: 'group-west', name: 'West group', customerId: 'customer-west' },
    { id: 'group-shared', name: 'Shared group', customerId: null }]
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Fixture operator', role: 'Operator', permissions: { overview: 1, phones: 4, memberships: 4 } } })
    if (request.headers().authorization !== 'Bearer ' + token) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/create-options') return route.fulfill({ json: { sites, groups, maximumGroups: 40,
      canAssignGroup: true, firmwareVersions: [{ id: 'fixture-firmware', label: 'Fixture firmware', buildId: null }],
      families: [{ id: 'mtr19-multipay', release: 'MTR 1.9', variant: 'Multi-pay', maximizerTypeNumbers: [28] }] } })
    if (request.method() === 'POST' || request.method() === 'PUT') {
      requests.push({ path: url.pathname, body: request.postDataJSON() })
      if (url.pathname === '/api/terminals') return route.fulfill({ status: 201, json: { id: 'fixture-phone' } })
    }
    return route.fulfill({ status: 403 })
  })
  await signIn(page)
  await page.goto('/terminals/new')
  await page.getByLabel('Phone group', { exact: true }).selectOption('group-east')
  await page.getByLabel('Additional group', { exact: true }).selectOption('group-shared')
  await page.getByRole('button', { name: 'Add group' }).click()
  await expect(page.getByText('Phone groups (2 of 40)')).toBeVisible()
  await page.getByTestId('terminal-create-site').selectOption('site-west')
  await expect(page.getByLabel('Phone group', { exact: true })).toHaveValue('group-shared')
  await page.getByLabel('Additional group', { exact: true }).selectOption('group-west')
  await page.getByRole('button', { name: 'Add group' }).click()
  await page.getByLabel('Installed firmware').selectOption('fixture-firmware')
  await page.getByLabel('Terminal family').selectOption('mtr19-multipay')
  await page.getByTestId('terminal-create-name').fill('Workshop terminal')
  await page.getByTestId('terminal-create-hex').fill('0102030405')
  expect(requests).toEqual([])
  await page.getByTestId('terminal-create-submit').click()
  await expect(page.getByText('Phone registered. You can register another phone.')).toBeVisible()
  expect(requests).toEqual([{ path: '/api/terminals', body: expect.objectContaining({
    siteId: 'site-west', terminalGroupId: 'group-shared', groupIds: ['group-shared', 'group-west'],
    firmwareVersionId: 'fixture-firmware', terminalFamilyId: 'mtr19-multipay', terminalIdHex: '0102030405', displayName: 'Workshop terminal',
  }) }])
})
