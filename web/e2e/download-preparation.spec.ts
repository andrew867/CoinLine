import { expect, test } from '@playwright/test'
import { signIn } from './auth'

for (const selectedOnly of [false, true]) test(`prepares ${selectedOnly ? 'selected records' : 'full configurations'} for a mixed phone list only after review`, async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('A local fixture origin is required.')
  const origin = new URL(baseURL).origin
  const token = 'fixture-preparation-token'
  const phones = [
    { id: '00000000-0000-4000-8000-000000000401', displayName: 'Workshop east', terminalIdHex: '0102030401', active: true },
    { id: '00000000-0000-4000-8000-000000000402', displayName: 'Workshop west', terminalIdHex: '0102030402', active: true },
    { id: '00000000-0000-4000-8000-000000000403', displayName: 'Closed workshop', terminalIdHex: '0102030403', active: false },
  ]
  const reviews = phones.map((phone, index) => ({ terminalId: phone.id, displayName: phone.displayName, terminalIdHex: phone.terminalIdHex,
    tableSetName: ['East advertising and coins', 'West advertising', 'Closed workshop set'][index],
    tableSetId: 'set-' + (index + 1), records: [29, ...(!index ? [50] : [])].map(tableNumber => ({ tableNumber })),
    eligible: phone.active, reason: phone.active ? null : 'Phone is deactivated.' }))
  const confirmed: unknown[] = []
  const previewed: unknown[] = []
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator', permissions: { overview: 1, downloads: 5 } } })
    if (request.headers().authorization !== 'Bearer ' + token) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/downloads' || url.pathname === '/api/tables/schedules') return route.fulfill({ json: [] })
    if (url.pathname === '/api/downloads/preparation/options')
      return route.fulfill({ json: { phones: url.searchParams.get('query') === 'west' ? [phones[1]] : [phones[0], phones[2]], more: false, maximumPhones: 40,
        recordOptions: [{ tableNumber: 29, name: 'Text advertising' }, { tableNumber: 50, name: 'Coin validation' }] } })
    if (url.pathname === '/api/downloads/preparation/preview' && request.method() === 'POST') {
      const body = request.postDataJSON() as { terminalIds: string[]; availableAtUtc: string | null }
      previewed.push(body)
      const selected = body.terminalIds.map(id => reviews.find(row => row.terminalId === id)!)
      return route.fulfill({ json: { phones: selected, eligibleCount: selected.filter(row => row.eligible).length,
        preparationAtUtc: body.availableAtUtc, previewToken: 'selected-' + selected.length } })
    }
    if (url.pathname === '/api/downloads/preparation' && request.method() === 'POST') {
      const body = request.postDataJSON(); confirmed.push(body)
      return route.fulfill({ json: { downloads: reviews.slice(0, 2).map((row, index) => ({ ...row,
        scheduleId: `schedule-${index}`, batchId: `batch-${index}`, availableAtUtc: '2031-06-01T12:00:00Z', preparedAtUtc: '2031-06-01T12:00:00Z' })) } })
    }
    return route.fulfill({ status: 403 })
  })
  await signIn(page)
  await page.goto('/downloads')
  await page.getByRole('button', { name: 'Prepare downloads for multiple phones' }).click()
  await page.getByRole('button', { name: /Add Workshop east/ }).click()
  await page.getByRole('button', { name: /Add Closed workshop/ }).click()
  await page.getByLabel('Search by phone ID or name').fill('west')
  await page.getByRole('button', { name: 'Search phones' }).click()
  await page.getByRole('button', { name: /Add Workshop west/ }).click()
  if (selectedOnly) {
    await page.getByRole('radio', { name: 'Selected records only' }).check()
    await expect(page.getByRole('button', { name: 'Review selected phones' })).toBeDisabled()
    await page.getByLabel('Record to add').selectOption('29')
    await page.getByRole('button', { name: 'Add record' }).click()
    await page.getByRole('button', { name: 'Save current selection' }).click()
    await expect(page.getByText('Standard saved records: MT29')).toBeVisible()
    await page.getByLabel('Preference', { exact: true }).selectOption('inmate')
    await page.getByRole('button', { name: 'Remove MT29' }).click()
    await page.getByLabel('Record to add').selectOption('50')
    await page.getByRole('button', { name: 'Add record' }).click()
    await page.getByRole('button', { name: 'Save current selection' }).click()
    await expect(page.getByText('Inmate saved records: MT50')).toBeVisible()
    await page.getByLabel('Preference', { exact: true }).selectOption('standard')
    await page.getByRole('button', { name: 'Load preference' }).click()
    await expect(page.getByRole('list', { name: 'Selected records' })).toContainText('MT29 · Text advertising')
    expect(previewed).toEqual([])
    expect(confirmed).toEqual([])
  }
  await page.getByRole('button', { name: 'Review selected phones' }).click()
  await expect(page.getByText('Phone is deactivated.')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Confirm preparation for 3 phones' })).toBeDisabled()
  expect(confirmed).toEqual([])
  await page.getByRole('button', { name: /Remove Closed workshop/ }).click()
  await page.getByRole('button', { name: 'Review selected phones' }).click()
  const east = page.getByRole('row').filter({ hasText: 'Workshop east' })
  const west = page.getByRole('row').filter({ hasText: 'Workshop west' })
  await expect(east).toContainText('East advertising and coins')
  await expect(east).toContainText('MT29, MT50')
  await expect(west).toContainText('West advertising')
  await expect(west).toContainText('MT29')
  await expect(west).not.toContainText('MT50')
  expect(confirmed).toEqual([])
  await page.getByRole('button', { name: 'Confirm preparation for 2 phones' }).click()
  await expect(page.getByRole('heading', { name: '2 download schedules created' })).toBeVisible()
  const links = page.getByRole('link', { name: 'Open prepared batch' })
  await expect(links.nth(0)).toHaveAttribute('href', '/downloads/batch-0')
  await expect(links.nth(1)).toHaveAttribute('href', '/downloads/batch-1')
  const scope = selectedOnly ? { recordNumbers: [29] } : {}
  expect(previewed).toEqual([{ terminalIds: [phones[0].id, phones[2].id, phones[1].id], availableAtUtc: null, ...scope },
    { terminalIds: [phones[0].id, phones[1].id], availableAtUtc: null, ...scope }])
  expect(confirmed).toEqual([{ terminalIds: [phones[0].id, phones[1].id], availableAtUtc: null, ...scope, previewToken: 'selected-2' }])
})
