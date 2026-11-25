import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('reviews a language worksheet and saves only chosen fixed wire rows', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('A local base URL is required.')
  const origin = new URL(baseURL).origin
  const token = 'fixture-prompt-operator'
  const set = '00000000-0000-4000-8000-000000001301'
  const fields = Array.from({ length: 400 }, (_, row) => ({
    key: `prompt-${row}.text`, label: row % 2 === 0 ? 'Upper line' : 'Lower line', editor: 'text',
    value: row === 1 ? null : row === 0 ? 'Café' : '', displayGlyphs: true, min: 0, max: 0, maxLength: 20, choices: null, help: `Wire row ${row}.`,
  }))
  let revision = 1
  const saves: Record<string, unknown>[] = []
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Fixture operator', role: 'Operator',
        permissions: { overview: 1, 'table-sets': 1, 'table-records': 3 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === `/api/tables/sets/${set}`) return route.fulfill({ json: {
      id: set, name: 'Language worksheet fixture', status: 0, revision, customerId: null,
      isDefault: false, publishedAtUtc: null, publishGeneration: 0, optionalRecordRemovals: [], targetFirmwareVersionId: null,
      targetFirmware: null, tableProfile: null, firmwareFindings: [], versions: [
        { id: 'fixture-prompt-85', tableDefinitionId: 'fixture-85', definitionName: 'Visual prompts',
          tableNumber: 85, tableRevision: revision, sortOrder: 1, validationPassed: true, payloadSha256Hex: 'a'.repeat(64),
          payloadLengthBytes: 8001, warnings: [] },
      ],
    } })
    if (url.pathname === `/api/tables/sets/${set}/records/85/fields`) {
      if (request.method() === 'PUT') {
        const body = request.postDataJSON()
        saves.push(body)
        for (const field of fields) if (field.key in body.changes) field.value = body.changes[field.key]
        revision++
        return route.fulfill({ status: 204 })
      }
      return route.fulfill({ json: { supported: true, editable: true, profile: 'Later packed profile', length: 8001, revision,
        sha256: 'a'.repeat(64), sections: Array.from({ length: 200 }, (_, index) => ({
          key: `message-${index}`, label: `Message ${index}`, showDisplayPreview: true, fields: fields.slice(index * 2, index * 2 + 2),
        })) } })
    }
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/terminals/families') return route.fulfill({ json: [] })
    if (url.pathname.endsWith('/fields')) return route.fulfill({ json: { supported: false, reason: 'Fixture record not present.' } })
    return route.fulfill({ status: 404 })
  })

  await signIn(page)
  await page.goto(`/table-sets/${set}?record=85`)
  await page.getByRole('button', { name: 'Visual prompt worksheet' }).click()
  await expect(page.getByText(/400 wire rows · 0 staged · 1 unmapped/)).toBeVisible()
  await page.getByRole('textbox', { name: /Row 0 text/ }).fill('Préparez')
  await page.getByRole('button', { name: 'Next rows' }).click()
  await page.getByRole('textbox', { name: /Row 25 text/ }).fill('Premier appel')
  await page.getByText('Import a worksheet or review supported glyphs', { exact: true }).click()
  const paste = page.getByLabel('Worksheet JSON', { exact: true })
  await paste.fill(JSON.stringify({ format: 'coinline-visual-prompts-v1', recordNumber: 86, rowCount: 400,
    rows: [{ row: 1, text: null }, { row: 399, text: 'Fin' }] }))
  await page.getByRole('button', { name: 'Review worksheet import' }).click()
  await expect(page.getByText('MT86 to MT85: 1 text changes, 399 retained rows.')).toBeVisible()
  await expect(page.getByText(/400 wire rows · 2 staged/)).toBeVisible()
  await page.getByRole('button', { name: 'Stage reviewed rows' }).click()
  await expect(page.getByText(/400 wire rows · 3 staged · 1 unmapped/)).toBeVisible()
  await page.getByRole('button', { name: 'Save record' }).click()
  await expect(page.getByText('Changes saved to this draft.')).toBeVisible()
  expect(saves).toHaveLength(1)
  expect(saves[0]).toMatchObject({ revision: 1, changes: { 'prompt-0.text': 'Préparez', 'prompt-25.text': 'Premier appel', 'prompt-399.text': 'Fin' } })
  expect(Object.keys(saves[0].changes as object)).toHaveLength(3)
  await page.reload()
  await page.getByRole('button', { name: 'Visual prompt worksheet' }).click()
  await expect(page.getByRole('textbox', { name: /Row 0 text/ })).toHaveValue('Préparez')
  await page.getByLabel('Find prompt row').fill('399')
  await expect(page.getByRole('textbox', { name: /Row 399 text/ })).toHaveValue('Fin')
})
