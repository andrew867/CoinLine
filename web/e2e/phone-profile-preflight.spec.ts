import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('excludes an incompatible phone from a full assignment but queues a compatible selected record', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin
  const token = 'fixture-profile-operator'
  const setId = '00000000-0000-4000-8000-000000003401'
  const groupId = '00000000-0000-4000-8000-000000003402'
  const phones = [
    { id: 'fixture-older', displayName: 'Older fixture phone' },
    { id: 'fixture-later', displayName: 'Later fixture phone' },
  ]
  const reason = 'MT92. The selected firmware uses 3,061 bytes. The record has 3,401.'
  const records = [
    { id: 'fixture-v30', tableDefinitionId: 'fixture-d30', definitionName: 'Display timing', tableNumber: 30, payloadLengthBytes: 68 },
    { id: 'fixture-v92', tableDefinitionId: 'fixture-d92', definitionName: 'Call screening', tableNumber: 92, payloadLengthBytes: 3401 },
  ].map((row, index) => ({ ...row, tableRevision: 1, sortOrder: index + 1, validationPassed: true,
    payloadSha256Hex: String(index + 1).repeat(64), warnings: [], configurationId: null }))
  const set = { id: setId, name: 'Mixed untargeted configuration', status: 1, revision: 1, customerId: null, isDefault: false,
    publishedAtUtc: '2026-01-01T00:00:00Z', publishGeneration: 1, optionalRecordRemovals: [], targetFirmwareVersionId: null,
    targetFirmware: null, tableProfile: null, firmwareFindings: [], versions: records }
  const assignmentRequests: unknown[] = []
  const queueRequests: unknown[] = []
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator',
        permissions: { overview: 1, phones: 1, 'table-sets': 1, 'table-assignments': 3, downloads: 5 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/terminals') return route.fulfill({ json: phones })
    if (url.pathname === '/api/tables/sets') return route.fulfill({ json: [set] })
    if (url.pathname === `/api/tables/sets/${setId}`) return route.fulfill({ json: set })
    if (url.pathname === '/api/tables/scope-options') return route.fulfill({ json:
      url.searchParams.get('targetType') === 'group' ? [{ id: groupId, name: 'Fixture group' }] : [] })
    if (url.pathname === `/api/tables/sets/${setId}/assignment-preview`) return route.fulfill({ json: {
      targets: phones.map(phone => ({ ...phone, eligible: phone.id === phones[0].id, terminalFamilyLabel: null,
        reason: phone.id === phones[0].id ? null : reason })),
    } })
    if (url.pathname === `/api/tables/sets/${setId}/assignments` && request.method() === 'POST') {
      assignmentRequests.push(request.postDataJSON())
      return route.fulfill({ json: { changed: 1, targetCount: 1 } })
    }
    if (url.pathname === `/api/terminals/${phones[1].id}/downloads` && request.method() === 'POST') {
      const body = request.postDataJSON(); queueRequests.push(body)
      if (body.scope === 'Full') return route.fulfill({ status: 400, json: { error: reason } })
      if (body.partialTableDefinitionIds?.length === 1 && body.partialTableDefinitionIds[0] === records[0].tableDefinitionId)
        return route.fulfill({ status: 201, json: { id: 'fixture-selected-batch' } })
      return route.fulfill({ status: 400, json: { error: 'Incorrect selection.' } })
    }
    if (url.pathname === '/api/downloads' || url.pathname.endsWith('/schedules') || url.pathname === '/api/terminals/families')
      return route.fulfill({ json: [] })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/table-sets/${setId}`)
  await page.getByLabel('Assign by').selectOption('group')
  await page.getByRole('combobox', { name: 'Phone group', exact: true }).selectOption(groupId)
  await page.getByRole('button', { name: 'Preview assignment' }).click()
  await expect(page.getByText('1 eligible phone')).toBeVisible()
  await expect(page.getByText(`Later fixture phone · ${reason}`, { exact: true })).toBeVisible()
  await page.getByRole('button', { name: 'Assign to 1 phone' }).click()
  await expect(page.getByText('Updated 1 of 1 eligible phone assignments.')).toBeVisible()
  expect(assignmentRequests).toEqual([{ targetType: 'group', targetId: groupId, terminalIds: [phones[0].id] }])

  await page.goto('/downloads')
  await page.getByLabel('Terminal', { exact: true }).selectOption(phones[1].id)
  await page.getByRole('button', { name: 'Create batch' }).click()
  await expect(page.getByRole('alert').filter({ hasText: reason })).toBeVisible()
  await expect(page.getByRole('link', { name: 'View queued batch' })).toHaveCount(0)
  await page.getByLabel('Records', { exact: true }).selectOption('Partial')
  await page.getByLabel('MT30 · Display timing').check()
  await expect(page.getByLabel('MT92 · Call screening')).not.toBeChecked()
  await page.getByRole('button', { name: 'Create batch' }).click()
  await expect(page.getByRole('link', { name: 'View queued batch' })).toHaveAttribute('href', '/downloads/fixture-selected-batch')
  expect(queueRequests).toEqual([
    { tableSetId: setId, scope: 'Full' },
    { tableSetId: setId, scope: 'Partial', partialTableDefinitionIds: [records[0].tableDefinitionId] },
  ])
})
