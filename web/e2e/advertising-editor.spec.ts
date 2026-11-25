import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('edits Western advertising glyphs and typed spares without rewriting unmapped imported text', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture requires a local base URL.')
  const origin = new URL(baseURL).origin
  const setId = '00000000-0000-4000-8000-000000000301'
  const endpoint = `/api/tables/sets/${setId}/advertising`
  const token = 'fixture-advertising-operator'
  const blank = () => ({ text: '', durationTicks: 0, mode: 0, blankBefore: false, endSequence: true,
    spareByte: 0, reservedFlags: 0, containsUnmappedGlyphs: false })
  let slots = Array.from({ length: 20 }, blank)
  slots[0] = { ...slots[0], text: 'A\uFFFD B', durationTicks: 125, mode: 3, blankBefore: true, endSequence: false,
    spareByte: 165, reservedFlags: 144, containsUnmappedGlyphs: true }
  slots[1] = { ...slots[1], text: 'Caf', durationTicks: 500, endSequence: false }
  let revision = 1
  const set = () => ({ id: setId, name: 'Workshop advertising', status: 0, revision: 1, customerId: null,
    isDefault: false, publishedAtUtc: null, publishGeneration: 0, optionalRecordRemovals: [], targetFirmwareVersionId: null,
    targetFirmware: null, tableProfile: null, firmwareFindings: [], versions: [{ id: 'fixture-version', tableDefinitionId: 'fixture-definition',
      definitionName: 'Text advertising', tableNumber: 29, tableRevision: revision, sortOrder: 1, validationPassed: true,
      payloadSha256Hex: 'a'.repeat(64), payloadLengthBytes: 481, warnings: [] }] })
  await page.route('**/*', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST') {
      return route.fulfill({ json: { token, displayName: 'Workshop operator', role: 'Operator',
        permissions: { overview: 1, 'table-sets': 1, 'table-records': 3 } } })
    }
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === `/api/tables/sets/${setId}`) return route.fulfill({ json: set() })
    if (url.pathname === endpoint) {
      if (request.method() === 'PUT') {
        slots = request.postDataJSON().slots
        revision++
        return route.fulfill({ status: 204 })
      }
      return route.fulfill({ json: { slots, revision, sha256: 'a'.repeat(64), onHookEnabled: true, quickAccessEnabled: false,
        callEstablishedEnabled: false, scrollTicks: 10 } })
    }
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto(`/table-sets/${setId}?record=29`)
  await page.getByText('Edit advertising messages', { exact: true }).click()
  const first = page.getByRole('group', { name: 'Message 1', exact: true })
  const second = page.getByRole('group', { name: 'Message 2', exact: true })
  await expect(first.getByText(/Imported text contains unmapped cells/)).toBeVisible()
  await first.getByText('Spare fields', { exact: true }).click()
  await expect(first.getByLabel('Spare attribute bit 4 (0x10)')).toBeChecked()
  await expect(first.getByLabel('Spare attribute bit 7 (0x80)')).toBeChecked()
  await first.getByLabel('Spare attribute bit 4 (0x10)').uncheck()
  await first.getByLabel('Spare attribute bit 7 (0x80)').uncheck()
  await first.getByLabel('Spare attribute bit 5 (0x20)').check()
  await first.getByLabel('Spare byte').fill('87')

  await page.getByText('Display character palette', { exact: true }).click()
  await page.getByLabel('Language palette').selectOption('french')
  await second.getByLabel('Text', { exact: true }).press('End')
  await page.getByRole('button', { name: 'Insert é into message 2' }).click()
  await expect(second.getByLabel('Text', { exact: true })).toHaveValue('Café')
  await expect(page.getByRole('img', { name: 'Terminal display, lower line Café' })).toBeVisible()
  await page.getByRole('button', { name: 'Save advertising' }).click()
  await expect(page.getByText('Advertising saved to this draft.')).toBeVisible()
  expect(slots[0]).toMatchObject({ text: 'A\uFFFD B', durationTicks: 125, mode: 3, blankBefore: true, spareByte: 87, reservedFlags: 32 })
  expect(slots[1]).toMatchObject({ text: 'Café', durationTicks: 500, spareByte: 0, reservedFlags: 0 })
  expect(slots.slice(2)).toEqual(Array.from({ length: 18 }, blank))

  await page.reload()
  await page.getByText('Edit advertising messages', { exact: true }).click()
  await expect(second.getByLabel('Text', { exact: true })).toHaveValue('Café')
  await first.getByText('Spare fields', { exact: true }).click()
  await expect(first.getByLabel('Spare byte')).toHaveValue('87')
  await expect(first.getByLabel('Spare attribute bit 5 (0x20)')).toBeChecked()
})
