import { expect, test } from '@playwright/test'
import { signIn } from './auth'

test('shows authorization decisions independently from recorded payments', async ({ page, baseURL }) => {
  if (!baseURL) throw new Error('A local fixture origin is required.')
  const origin = new URL(baseURL).origin
  const token = 'fixture-payment-reader'
  const common = {
    terminalId: 'fixture-phone', terminalDisplayName: 'Synthetic workshop phone', nccSessionId: 'fixture-session',
    createdAtUtc: '2026-01-01T12:00:00Z', messageType: 2, provider: 'synthetic_lab', reason: null,
    cardAccountId: null, quoteId: null, quotedAmountUsd: null, callSequence: null,
  }
  const attempts = [
    { ...common, id: 'attempt-approved', outcome: 'approved', reason: 'approved', quotedAmountUsd: 0.05,
      cardAccountId: 'fixture-test-account', quoteId: 'fixture-quote', callSequence: 1 },
    { ...common, id: 'attempt-denied', outcome: 'declined', reason: 'quote_missing' },
    { ...common, id: 'attempt-unknown', outcome: 'unknown', provider: 'unrecorded' },
  ]
  let loaded = 0
  let posted = false
  await page.route('**/*', async route => {
    const request = route.request()
    const url = new URL(request.url())
    if (url.origin !== origin) return route.abort()
    if (!url.pathname.startsWith('/api/')) return route.continue()
    if (url.pathname === '/api/auth/login' && request.method() === 'POST')
      return route.fulfill({ json: { token, displayName: 'Payment reader', role: 'Operator',
        permissions: { overview: 1, 'card-transactions': 1 } } })
    if (request.headers().authorization !== `Bearer ${token}`) return route.fulfill({ status: 401 })
    if (url.pathname === '/api/cards/authorization-attempts') {
      loaded++
      return route.fulfill({ json: attempts })
    }
    if (url.pathname === '/api/cards/transactions') return route.fulfill({ json: posted ? [{
      id: 'fixture-payment', cardAccountId: 'fixture-test-account', amount: -0.05, reconciliation: 1,
      reportedCardType: 1, createdAtUtc: common.createdAtUtc, source: 'synthetic_cdr',
    }] : [] })
    if (url.pathname === '/api/operator/dashboard') return route.fulfill({ json: { terminalsByStatus: [], activeCraftSessions: 0,
      openNccSessions: 0, failedDownloads: 0, uploadAlerts: { failed: 0, quarantined: 0 } } })
    return route.fulfill({ status: 404 })
  })
  await signIn(page)
  await page.goto('/payment-transactions')
  const history = page.getByRole('region', { name: 'Authorization history' })
  const approved = history.getByRole('row').filter({ hasText: 'Eligible test fixture and quote' })
  await expect(approved).toContainText('Synthetic workshop phone')
  await expect(approved).toContainText('Approved')
  await expect(approved).toContainText('USD 0.05')
  await expect(approved.getByRole('link')).toHaveCount(0)
  await expect(history.getByText('No recent matching quote')).toBeVisible()
  await expect(history.getByText('Outcome unavailable', { exact: true })).toBeVisible()
  await expect(page.getByText('No payment transactions recorded.')).toBeVisible()
  await page.getByLabel('Authorization result').selectOption('not-approved')
  await expect(history.getByRole('row')).toHaveCount(2)
  await expect(history.getByText('No recent matching quote')).toBeVisible()
  await expect(approved).toHaveCount(0)
  await page.getByLabel('Authorization result').selectOption('approved')
  await expect(approved).toBeVisible()
  const beforeRefresh = loaded
  await page.getByRole('button', { name: 'Refresh payment history' }).click()
  await expect(approved).toBeVisible()
  await expect.poll(() => loaded).toBeGreaterThan(beforeRefresh)
  await expect(page.getByText('No payment transactions recorded.')).toBeVisible()
  posted = true
  await page.getByRole('button', { name: 'Refresh payment history' }).click()
  const ledger = page.getByRole('region', { name: 'Payment ledger entries' })
  const entry = ledger.getByRole('row').filter({ hasText: 'Simulated call-detail posting' })
  await expect(entry).toContainText('-0.05')
  await expect(entry).toContainText('Pending')
  await expect(approved).toBeVisible()
})
