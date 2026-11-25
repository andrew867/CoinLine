import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('reviews a scoped prefix cutover and exposes the exact blocked replacement without scheduling', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('A local fixture origin is required.')
  const origin = new URL(baseURL).origin
  const token = 'fixture-cutover-operator'
  const group = '00000000-0000-4000-8000-000000000301'
  const phoneIds = ['00000000-0000-4000-8000-000000000302', '00000000-0000-4000-8000-000000000303']
  const setId = '00000000-0000-4000-8000-000000000304'
  const requests: string[] = []
  const mutations: string[] = []
  const targets = phoneIds.map((id, index) => ({
    id, displayName: index ? 'Second workshop phone' : 'First workshop phone',
    currentId: '000001000' + (index + 1), replacementId: '000002000' + (index + 1),
    tableSetId: setId, tableSetName: 'Assigned workshop configuration', tableSetRevision: 2,
    accessRevision: 3, accessLength: 48, sourceHash: 'a'.repeat(64), replacementHash: 'b'.repeat(64),
    eligible: !index, issues: index ? ['Another phone already uses the replacement ID, including inactive inventory.'] : [],
  }))
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator',
        permissions: { overview: 1, phones: 1, 'table-assignments': 1, 'table-records': 1 } } })
    if (request.headers().authorization !== 'Bearer ' + token) return route.fulfill({ status: 401 })
    if (request.method() !== 'GET') { mutations.push(request.method() + ' ' + url.pathname); return route.fulfill({ status: 405 }) }
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals') return route.fulfill({ json: targets.map(row =>
      ({ id: row.id, displayName: row.displayName, terminalIdHex: row.currentId, status: 1, onCall: false })) })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    if (url.pathname === '/api/tables/scope-options') return route.fulfill({ json: [{ id: group, name: 'Workshop group' }] })
    if (url.pathname === '/api/terminals/identity-cutover/preview') {
      requests.push(url.search)
      return route.fulfill({ json: { eligibleCount: 1, blockedCount: 1, scheduled: false, targets } })
    }
    return route.fulfill({ status: 404 })
  })

  await signIn(page)
  await page.goto('/terminals')
  await page.getByRole('link', { name: 'Review terminal ID cutover' }).click()
  await page.getByLabel('Existing ID or prefix').fill('000001')
  await page.getByLabel('Replacement ID or prefix').fill('000002')
  await page.getByLabel('Inventory scope').selectOption('group')
  await page.getByLabel('Phone group').selectOption(group)
  await page.getByRole('button', { name: 'Preview cutover' }).click()
  await expect(page.getByRole('status')).toHaveText('1 eligible, 1 blocked. Nothing scheduled or changed.')
  const first = page.getByRole('row').filter({ hasText: 'First workshop phone' })
  await expect(first).toContainText('0000020001')
  await expect(first).toContainText('MT21 revision 3, 48 bytes')
  await first.getByText('Record fingerprints').click()
  await expect(first).toContainText('b'.repeat(64))
  const second = page.getByRole('row').filter({ hasText: 'Second workshop phone' })
  await expect(second).toContainText('0000020002')
  await expect(second).toContainText('Another phone already uses the replacement ID')
  expect(requests).toEqual(['?source=000001&replacement=000002&scope=group&targetId=' + group])
  expect(mutations).toEqual([])
  await page.getByLabel('Replacement ID or prefix').fill('000003')
  await expect(page.getByRole('status')).toHaveCount(0)
})
