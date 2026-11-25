import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('reviews the existing build and applies only the reviewed compatible profile', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin
  const id = '00000000-0000-4000-8000-000000003002'
  const token = 'fixture-registry-operator'
  const older = 'mtr19-multipay-200'
  const later = 'mtr2x-multipay-180-double-compressed'
  const profiles = [
    { id: older, label: '200-entry layout', verifiedLengths: { 92: 3401 }, terminalAcknowledgedRecords: [92], completeCatalogue: false },
    { id: later, label: '180-entry layout', verifiedLengths: { 92: 3061 }, terminalAcknowledgedRecords: [92], completeCatalogue: true },
  ]
  let appliedProfile: string | null = null
  const writes: unknown[] = []
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator',
        permissions: { overview: 1, 'firmware-registry': 3 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/firmware/versions' && request.method() === 'GET')
      return route.fulfill({ json: [{ id, label: 'Installed fixture build', buildId: 'fixture', tableProfileId: appliedProfile }] })
    if (url.pathname === '/api/firmware/table-profiles') return route.fulfill({ json: profiles })
    if (url.pathname === `/api/firmware/versions/${id}/table-profile/preview`) {
      const selected = request.postDataJSON().tableProfileId
      const blocked = selected === later
      return route.fulfill({ json: {
        firmwareVersionId: id, revision: 1, currentProfileId: appliedProfile, proposedProfileId: selected,
        phoneCount: 4, targetedSetCount: 2, assignedSetCount: 1, pendingBatchCount: 1,
        scheduledDownloadCount: 1, activeSessionCount: 0, activeBatchCount: 0,
        changed: true, canApply: !blocked, previewToken: 'fixture-review-token',
        findings: [{ tableNumber: 92, scope: blocked ? 'Assigned sets' : 'Targeted drafts', count: 1,
          blocking: blocked, message: blocked ? 'The record has 3,401 bytes.' : 'Draft requires a length review.' }],
      } })
    }
    if (url.pathname === `/api/firmware/versions/${id}/table-profile` && request.method() === 'PUT') {
      const body = request.postDataJSON()
      writes.push(body)
      if (body.tableProfileId !== older || body.previewToken !== 'fixture-review-token') return route.fulfill({ status: 409 })
      appliedProfile = body.tableProfileId
      return route.fulfill({ json: { id, tableProfileId: appliedProfile, revision: 2, changed: true } })
    }
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto('/firmware/versions')
  const row = page.getByRole('row').filter({ hasText: 'Installed fixture build' })
  await expect(row.getByText('Unverified layout')).toBeVisible()
  await expect(page.getByRole('button', { name: 'Register firmware' })).toHaveCount(0)
  await row.getByRole('button', { name: 'Review profile change' }).click()
  const editor = page.getByRole('region', { name: 'Review firmware table profile' })
  await editor.getByLabel('Proposed table layout').selectOption(later)
  await editor.getByRole('button', { name: 'Preview impact' }).click()
  await expect(editor.getByRole('row').filter({ hasText: 'MT92' })).toContainText('Blocking')
  await expect(editor.getByText('The record has 3,401 bytes.')).toBeVisible()
  await expect(editor.getByRole('button', { name: 'Apply reviewed profile' })).toBeDisabled()
  await editor.getByLabel('Proposed table layout').selectOption(older)
  await expect(editor.getByRole('button', { name: 'Apply reviewed profile' })).toHaveCount(0)
  await editor.getByRole('button', { name: 'Preview impact' }).click()
  await expect(editor.getByRole('row').filter({ hasText: 'MT92' })).toContainText('Advisory')
  await editor.getByRole('button', { name: 'Apply reviewed profile' }).click()
  await expect(page.getByText('Firmware table profile updated.')).toBeVisible()
  await expect(row.getByText('200-entry layout')).toBeVisible()
  expect(writes).toEqual([{ tableProfileId: older, previewToken: 'fixture-review-token' }])
  await page.reload()
  await expect(row.getByText('200-entry layout')).toBeVisible()
})
