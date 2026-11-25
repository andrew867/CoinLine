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
  await page.getByRole('combobox', { name: 'Phone group', exact: true }).selectOption(group)
  await page.getByRole('button', { name: 'Preview cutover' }).click()
  await expect(page.getByRole('status')).toHaveText('1 eligible, 1 blocked. Nothing scheduled or changed.')
  const first = page.getByRole('row').filter({ hasText: 'First workshop phone' })
  await expect(first).toContainText('0000020001')
  await expect(first).toContainText('MT21 revision 3, 48 bytes')
  await first.getByText('Record fingerprints').click()
  await expect(first.getByText('b'.repeat(64), { exact: true })).toBeVisible()
  const second = page.getByRole('row').filter({ hasText: 'Second workshop phone' })
  await expect(second).toContainText('0000020002')
  await expect(second).toContainText('Another phone already uses the replacement ID')
  expect(requests).toEqual(['?source=000001&replacement=000002&scope=group&targetId=' + group])
  expect(mutations).toEqual([])
  await page.getByLabel('Replacement ID or prefix').fill('000003')
  await expect(page.getByRole('status')).toHaveCount(0)
})

test('schedules a reserved identity, cancels unsent work and reconciles an independently observed replacement', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('A local fixture origin is required.')
  const origin = new URL(baseURL).origin
  const token = 'fixture-cutover-manager'
  const path = '/api/terminals/identity-cutover'
  const phoneId = '00000000-0000-4000-8000-000000000401'
  const base = { id: phoneId, displayName: 'Workshop phone', currentId: '0000010001', replacementId: '0000020001',
    tableSetId: 'fixture-set', tableSetName: 'Workshop configuration', tableSetRevision: 1, accessRevision: 2,
    accessLength: 18, sourceHash: 'a'.repeat(64), replacementHash: 'b'.repeat(64), eligible: true, issues: [] }
  const queued = { id: 'fixture-queued', jobId: 'fixture-job', terminalId: phoneId, sourceWireId: base.currentId,
    targetWireId: base.replacementId, dueAtUtc: '2026-12-01T00:00:00Z', state: 'scheduled', revision: 0, canCancel: true }
  const observed = { ...queued, id: 'fixture-observed', terminalId: 'fixture-other-phone', sourceWireId: '0000010002',
    targetWireId: '0000020002', state: 'activation_blocked', revision: 6, canCancel: false,
    activationError: 'The reviewed assignment changed.', newIdentityObservedAtUtc: '2026-12-01T01:00:00Z' }
  let items: (typeof queued & { activationError?: string | null; newIdentityObservedAtUtc?: string })[] = [observed]
  const writes: { path: string; body: unknown }[] = []
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Workshop manager', role: 'Operator',
        permissions: { overview: 1, phones: 3, 'table-assignments': 3, 'table-records': 1, downloads: 4 } } })
    if (request.headers().authorization !== 'Bearer ' + token) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === path + '/preview') return route.fulfill({ json: { eligibleCount: 1, blockedCount: 0,
      scheduled: false, reviewToken: 'reviewed-snapshot', targets: [base] } })
    if (url.pathname === path + '/jobs') {
      if (request.method() === 'GET') return route.fulfill({ json: items })
      writes.push({ path: url.pathname, body: request.postDataJSON() })
      items = [queued, observed]
      return route.fulfill({ status: 201, json: { jobId: queued.jobId, items: [queued] } })
    }
    if (url.pathname.endsWith('/cancel')) {
      writes.push({ path: url.pathname, body: request.postDataJSON() })
      items = [{ ...queued, state: 'cancelled', revision: 1, canCancel: false }, observed]
      return route.fulfill({ json: items[0] })
    }
    if (url.pathname.endsWith('/recovery')) return route.fulfill({ json: {
      revision: observed.revision, assignmentId: 'fixture-assignment', assignmentRevision: 2,
      currentSetId: 'fixture-newer', currentSetName: 'Current configuration', currentIssue: 'The access record has the old ID.',
      stagedSetId: 'fixture-staged', stagedSetName: 'Dispatched replacement', stagedIssue: null } })
    if (url.pathname.endsWith('/reconcile')) {
      writes.push({ path: url.pathname, body: request.postDataJSON() })
      items = [items[0], { ...observed, state: 'activated', revision: 7, activationError: null }]
      return route.fulfill({ json: items[1] })
    }
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto('/terminals/identity-cutover')
  await page.getByLabel('Existing ID or prefix').fill('0000010001')
  await page.getByLabel('Replacement ID or prefix').fill('0000020001')
  await page.getByRole('button', { name: 'Preview cutover' }).click()
  await expect(page.getByLabel('Select Workshop phone')).toBeChecked()
  await page.getByRole('button', { name: 'Schedule cutover for 1 phone' }).click()
  await expect(page.getByText(/Scheduled 1 phone/)).toBeVisible()
  const scheduledRow = page.getByRole('row').filter({ hasText: '0000020001' })
  const conflictRow = page.getByRole('row').filter({ hasText: '0000020002' })
  await expect(scheduledRow).toContainText('Scheduled, awaiting ordinary call-in')
  await expect(conflictRow).toContainText('New ID observed; reconciliation required')
  await expect(conflictRow.getByRole('button', { name: 'Cancel cutover' })).toHaveCount(0)
  await scheduledRow.getByRole('button', { name: 'Cancel cutover' }).click()
  await expect(scheduledRow).toContainText('Cancelled before possible application')
  await conflictRow.getByRole('button', { name: 'Review reconciliation' }).click()
  await expect(page.getByLabel('Keep current assignment')).toBeDisabled()
  await expect(page.getByRole('button', { name: 'Confirm reviewed replacement' })).toBeDisabled()
  await page.getByLabel('Use the dispatched replacement set').check()
  await page.getByRole('button', { name: 'Confirm reviewed replacement' }).click()
  await expect(conflictRow).toContainText('New identity confirmed')
  expect(writes).toEqual([
    { path: path + '/jobs', body: { mapping: { source: base.currentId, replacement: base.replacementId, scope: 'all' },
      terminalIds: [phoneId], reviewToken: 'reviewed-snapshot', dueAtUtc: null } },
    { path: path + '/jobs/fixture-job/items/fixture-queued/cancel', body: { revision: 0 } },
    { path: path + '/jobs/fixture-job/items/fixture-observed/reconcile', body: { choice: 'staged', revision: 6,
      assignmentId: 'fixture-assignment', assignmentRevision: 2 } },
  ])
})
