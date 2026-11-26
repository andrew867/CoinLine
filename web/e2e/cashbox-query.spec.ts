import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('finds a reported cashbox, browses to a collection, and opens a phone-specific query from Terminal status', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin, token = 'fixture-cashbox-operator'
  const phones = ['Fixture Alpha', 'Fixture Beta'].map((displayName, index) => ({
    id: `00000000-0000-4000-8000-00000000000${index + 1}`, displayName, terminalIdHex: `000000000${index + 1}`,
    groupName: null, onCall: false, lastSessionAtUtc: null,
  }))
  const receivedAtUtc = '2028-10-01T12:00:00Z', terminalTime = '2028-10-01T08:00:00'
  const status = { boxNumber: '00000001', terminalTime, balanceMinorUnits: 75, percentFull: 40,
    condition: 'Value threshold exceeded', unmappedSetBitCount: 0, coinCounts: [0, 0, 0, 3] }
  const lockedReads: string[] = []
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST') return route.fulfill({ json: {
      token, role: 'Operator', displayName: 'Fixture operator', permissions: { overview: 1, cashbox: 1, 'phone-status': 1, phones: 1 },
    } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (request.method() !== 'GET') return route.fulfill({ status: 405 })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals') return route.fulfill({ json: phones })
    if (url.pathname === '/api/field-events') return route.fulfill({ json: { items: [] } })
    if (url.pathname === '/api/field-events/phone-options') return route.fulfill({ json: phones })
    if (url.pathname.endsWith('/current-status')) return route.fulfill({ json: {
      ...phones[0], deactivatedAtUtc: null, latestReportAtUtc: null, statusReportId: null, statusReportedAtUtc: null,
      newerUnreadableReport: false, serialNumber: null, groups: null, hasStatusBits: null, unmappedSetBitCount: null,
    } })
    if (url.pathname === '/api/cashbox/phones') {
      const locked = url.searchParams.get('terminalId')
      if (locked) lockedReads.push(locked)
      const result = locked ? phones.filter(phone => phone.id === locked) : phones
      return route.fulfill({ json: { phones: result, total: result.length, page: 1, pageSize: 50 } })
    }
    if (url.pathname === `/api/terminals/${phones[0].id}/cashbox`) return route.fulfill({ json: {
      statuses: [{ id: 'alpha-status', receivedAtUtc, status, isCurrent: true }], collections: [],
    } })
    if (url.pathname === `/api/terminals/${phones[1].id}/cashbox`) return route.fulfill({ json: {
      statuses: [{ id: 'beta-status', receivedAtUtc, status, isCurrent: false }], collections: [{
        id: 'beta-collection', receivedAtUtc, terminalTime, boxNumber: '00000001', newBoxNumber: '00000002',
        totalMinorUnits: 125, removedBoxBalanceMinorUnits: 75, isSummed: true, coinCounts: [0, 0, 0, 3],
      }],
    } })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.getByRole('link', { name: 'Cashbox status' }).click()
  await expect(page.getByRole('combobox', { name: 'Phone' })).toHaveValue(phones[0].id)
  const current = page.getByRole('region', { name: 'Current reported cashbox' })
  await expect(current.getByText('0.75')).toBeVisible()
  await expect(current.getByText('Value threshold exceeded')).toBeVisible()
  await expect(current.getByText('Slot 4: 3', { exact: false })).toBeVisible()
  await page.getByRole('button', { name: 'Find next' }).click()
  await expect(page.getByRole('combobox', { name: 'Phone' })).toHaveValue(phones[1].id)
  await expect(page.getByText(/No current box status is available/)).toBeVisible()
  await expect(page.getByRole('table', { name: 'Reported collections' }).getByText('1.25 (combined)')).toBeVisible()
  await expect(current).toHaveCount(0)
  await page.goto(`/field-events?terminalId=${phones[0].id}`)
  await page.getByRole('link', { name: 'Query cashbox' }).click()
  await expect(page.getByRole('heading', { name: 'Selected phone' })).toBeVisible()
  await expect(page.getByText(phones[0].displayName, { exact: true })).toBeVisible()
  await expect(current.getByText('0.75')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Find next' })).toHaveCount(0)
  await expect(page.getByRole('link', { name: 'Browse all cashboxes' })).toBeVisible()
  expect(lockedReads).toEqual([phones[0].id])
})
