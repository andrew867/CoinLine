import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('stages an exact display timer in seconds and reopens only that changed MT30 field', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin
  const token = 'fixture-interface-timing-operator'
  const id = '00000000-0000-4000-8000-000000003001'
  let revision = 1
  let scroll = '10'
  const saves: Record<string, unknown>[] = []
  const field = (key: string, label: string, value: string) => ({ key, label: `${label} (10 ms ticks)`, value,
    editor: 'number', min: 0, max: 32767, maxLength: null, choices: null, help: null })
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator',
        permissions: { overview: 1, 'table-sets': 1, 'table-records': 3 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === `/api/tables/sets/${id}`) return route.fulfill({ json: {
      id, name: 'Interface timing draft', status: 0, revision: 1, customerId: null, isDefault: false, publishedAtUtc: null,
      publishGeneration: 0, optionalRecordRemovals: [], targetFirmwareVersionId: null, targetFirmware: null,
      tableProfile: null, firmwareFindings: [], versions: [{ id: 'fixture-version', tableDefinitionId: 'fixture-definition',
        definitionName: 'User interface parameters', tableNumber: 30, tableRevision: revision, sortOrder: 1, validationPassed: true,
        payloadSha256Hex: String(revision).repeat(64), payloadLengthBytes: 68, warnings: [] }],
    } })
    if (url.pathname === `/api/tables/sets/${id}/records/30/fields`) {
      if (request.method() === 'PUT') {
        const body = request.postDataJSON(); saves.push(body); scroll = body.changes.scrollSpeed ?? scroll; revision++
        return route.fulfill({ status: 204 })
      }
      return route.fulfill({ json: { supported: true, editable: true, profile: 'MTR 1.9 packed', length: 68,
        revision, sha256: String(revision).repeat(64), sections: [{ key: 'prompts', label: 'Prompt and input timing', fields: [
          field('clearDigits', 'Clear digits after', '450'), field('scrollSpeed', 'Display scroll timing', scroll),
        ] }, { key: 'calls', label: 'Other timing', fields: [field('manualHold', 'Manual dialing hold', '450')] }] } })
    }
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/table-sets/${id}?record=30`)
  const timing = page.getByRole('region', { name: 'Interface timing worksheet' })
  await expect(timing.getByText('0.10 s')).toBeVisible()
  await expect(timing.getByText('4.50 s')).toBeVisible()
  await timing.getByLabel('New duration (seconds)').fill('0.255')
  await expect(timing.getByRole('button', { name: 'Stage exact ticks' })).toBeDisabled()
  await timing.getByLabel('New duration (seconds)').fill('0.25')
  await timing.getByRole('button', { name: 'Stage exact ticks' }).click()
  await expect(timing.getByText('0.25 s')).toBeVisible()
  await expect(page.getByLabel('Display scroll timing (10 ms ticks)')).toHaveValue('25')
  await page.getByLabel('Section or entry').selectOption('calls')
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(saves).toHaveLength(1)
  expect(saves[0]).toMatchObject({ revision: 1, sha256: '1'.repeat(64), changes: { scrollSpeed: '25' } })
  expect(saves[0].changes).toEqual({ scrollSpeed: '25' })
  await page.reload()
  await expect(timing.getByText('0.25 s')).toBeVisible()
  await expect(timing.getByText('4.50 s')).toBeVisible()
  await page.getByLabel('Section or entry').selectOption('calls')
  await expect(page.getByLabel('Manual dialing hold (10 ms ticks)')).toHaveValue('450')
})
