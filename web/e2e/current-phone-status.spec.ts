import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('queries the current reported phone state independently of historic alarms', async ({ page, baseURL }, testInfo) => {
  if (!baseURL) throw new Error('A local fixture origin is required.')
  const origin = new URL(baseURL).origin
  const token = 'fixture-status-reader'
  const phone = { id: '00000000-0000-4000-8000-000000000501', displayName: 'Workshop phone', terminalIdHex: '0102030405',
    groupName: 'Workshop group', onCall: false, lastSessionAtUtc: '2026-10-01T12:05:03Z' }
  const unsupported: string[] = []
  let cleared = false
  await page.route('**/*', async route => {
    const req = route.request(), url = new URL(req.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && req.method() === 'POST') return route.fulfill({ json: {
      token, name: 'Fixture status operator', displayName: 'Fixture status operator', role: 'Operator', permissions: { overview: 1, 'phone-status': 1 } } })
    if (req.headers().authorization !== 'Bearer ' + token) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    // Existing Home summary requests are outside this phone-status workflow.
    if (url.pathname === '/api/cards/reconciliation-pending' || url.pathname === '/api/sip-modem') return route.fulfill({ status: 403 })
    if (url.pathname === '/api/field-events/phone-options') return route.fulfill({ json: [phone] })
    if (url.pathname === '/api/field-events/reference') return route.fulfill({ json: { alarms: [], maintenance: [], canEdit: false } })
    if (url.pathname === `/api/field-events/phones/${phone.id}/current-status`) return route.fulfill({ json: {
      ...phone, deactivatedAtUtc: null,
      latestReportAtUtc: cleared ? '2026-10-01T12:06:00Z' : '2026-10-01T12:05:03Z',
      statusReportId: cleared ? 'report-clear' : 'report-prior', statusReportedAtUtc: cleared ? '2026-10-01T12:06:00Z' : '2026-10-01T12:00:00Z',
      newerUnreadableReport: !cleared, serialNumber: '0123456789', hasStatusBits: !cleared, unmappedSetBitCount: cleared ? 0 : 2,
      statusProfile: 'Later', rawStatusHex: cleared ? '0000000000' : '0400404090',
      groups: [{ label: 'Handset and telephony', rawMask: cleared ? 0 : 4, flags: [{ name: 'EPM/SAM not responding', asserted: !cleared, statusIndex: 2 }], unresolvedFlags: [] },
        { label: 'Memory and stored records', rawMask: cleared ? 0 : 64, flags: [{ name: 'Memory lost', asserted: !cleared }, { name: 'EEPROM fault', asserted: false }] },
        { label: 'Enclosure and cash box', rawMask: cleared ? 0 : 64, flags: [{ name: 'Cash box full', asserted: !cleared }] },
        { label: 'Coin equipment and line', rawMask: cleared ? 0 : 144, flags: [], unresolvedFlags: cleared ? [] : [
          { statusIndex: 36, mask: 16, description: 'Reserved or unverified status bit' }, { statusIndex: 39, mask: 128, description: 'Reserved or unverified status bit' } ] }]
    } })
    if (url.pathname === '/api/field-events') return route.fulfill({ json: { items: [{ id: 'old-alarm', terminalId: phone.id,
      terminalName: phone.displayName, title: 'Alarm', summary: 'Memory lost (alarm 22)', category: 'alarm', capturedAtUtc: '2026-10-01T12:00:00Z' }] } })
    unsupported.push(url.pathname)
    return route.fulfill({ status: 403 })
  })
  await signIn(page)
  await page.goto('/field-events')
  await page.getByRole('combobox', { name: 'Phone', exact: true }).selectOption(phone.id)
  await expect(page.getByRole('heading', { name: 'Latest reported phone status' })).toBeVisible()
  await expect(page.getByText('0123456789')).toBeVisible()
  await expect(page.getByText('2026-10-01 12:00:00 UTC')).toBeVisible()
  await expect(page.getByRole('note')).toContainText('A more recent MT10 could not be interpreted.')
  await expect(page.getByText('2 unmapped status bits set.', { exact: false })).toBeVisible()
  await expect(page.getByText('Registered later profile.')).toBeVisible()
  await expect(page.getByText('04 00 40 40 90')).toBeVisible()
  await expect(page.getByRole('region', { name: 'Handset and telephony' }).getByRole('listitem').filter({ hasText: 'EPM/SAM not responding' }).getByText('Reported', { exact: true })).toBeVisible()
  await expect(page.getByText('Status bit 39 (0x80)')).toBeVisible()
  const memory = page.getByRole('region', { name: 'Memory and stored records' })
  await expect(memory.getByRole('listitem').filter({ hasText: 'Memory lost' }).getByText('Reported', { exact: true })).toBeVisible()
  await expect(memory.getByRole('listitem').filter({ hasText: 'EEPROM fault' }).getByText('Not indicated', { exact: true })).toBeVisible()
  await expect(page.getByText('Memory lost (alarm 22)', { exact: true })).toBeVisible()
  await expect(page.getByRole('link', { name: 'Open source status report' })).toHaveCount(0)
  await page.screenshot({ path: testInfo.outputPath('status-review.png'), fullPage: true })
  cleared = true
  await page.getByRole('button', { name: 'Refresh', exact: true }).click()
  await expect(page.getByText('2026-10-01 12:06:00 UTC')).toBeVisible()
  await expect(memory.getByRole('listitem').filter({ hasText: 'Memory lost' }).getByText('Not indicated', { exact: true })).toBeVisible()
  await expect(page.getByText('No named conditions reported')).toBeVisible()
  await expect(page.getByText('Memory lost (alarm 22)', { exact: true })).toBeVisible()
  expect(unsupported).toEqual([])
})
