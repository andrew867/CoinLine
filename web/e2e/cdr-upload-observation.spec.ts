import { test, expect } from '@playwright/test'
import { signIn } from './auth'

test('explains the status-only upload and unavailable cashbox restore without asserting the terminal has no records', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('The console fixture needs a local base URL.')
  const origin = new URL(baseURL).origin, token = 'fixture-upload-observer', id = 'fixture-ncc-contact'
  const consoleErrors: string[] = []
  page.on('console', message => { if (message.type() === 'error') consoleErrors.push(message.text()) })
  await page.route('**/*', async route => {
    const request = route.request(), url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login') return route.fulfill({ json: { token, displayName: 'Fixture operator', role: 'Operator', permissions: { overview: 1, 'ncc-sessions': 1 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], openNccSessions: 0, activeCraftSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    if (url.pathname === '/api/cards/reconciliation-pending') return route.fulfill({ json: { paymentCount: 0, paymentTotal: 0, callCount: 0 } })
    if (url.pathname === '/api/field-events') return route.fulfill({ json: { total: 0, items: [] } })
    if (url.pathname === '/api/sip-modem') return route.fulfill({ json: { listening: false } })
    if (url.pathname === '/api/ncc/sessions') return route.fulfill({ json: [{ id, terminalId: 'fixture-phone', status: 1, socketBound: false, correlationId: 'Synthetic CDR session',
      startedAtUtc: '2028-01-01T01:00:00Z', endedAtUtc: '2028-01-01T01:00:10Z', lastFrameSampleHex: null }] })
    if (url.pathname === `/api/ncc/sessions/${id}/upload-observations`) return route.fulfill({ json: {
      cashboxRestores: [{ observedAtUtc: '2028-01-01T01:00:05Z', available: false, sent: true, responseBytes: 1 }],
      callUploadFrames: [{ sequence: 11, parsed: true, endsDialogue: true, records: [
        { messageType: 10, byteCount: 11 }, { messageType: 60, byteCount: 28 }, { messageType: 13, byteCount: 1 }] }] } })
    if (url.pathname === `/api/ncc/sessions/${id}` && url.searchParams.get('clockOnly') === 'true') return route.fulfill({ json: { clockOffers: [] } })
    return route.fulfill({ status: 404 })
  })
  await signIn(page); await page.goto(`/ncc-sessions?sessionId=${id}`)
  const row = page.getByRole('row').filter({ hasText: 'Synthetic CDR session' })
  await expect(row.getByText('Closed', { exact: true })).toBeVisible()
  await row.getByRole('button', { name: 'Inspect upload records' }).click()
  const section = page.getByRole('region', { name: 'Call-upload observations' })
  await expect(section.getByText(/No current cashbox status was known/)).toBeVisible()
  await expect(section.getByText('0 call-detail records observed')).toBeVisible()
  await expect(section.getByText('End of data (MT13), 1 byte')).toBeVisible()
  await expect(section.getByText(/not a statement about records remaining in terminal storage/)).toBeVisible()
  await expect(page.getByText(/No retained clock offer for this session/)).toBeVisible()
  expect(consoleErrors).toEqual([])
  await section.screenshot({ path: test.info().outputPath('upload-observations.png') })
})
