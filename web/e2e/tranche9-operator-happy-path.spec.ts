import { test, expect } from '@playwright/test'

/**
 * Full operator-console happy path (requires API on :5006 proxied via Vite).
 * Creates disposable customer/site/terminal rows — safe on dev databases only.
 */
test('tranche 9 operator happy path', async ({ page }) => {
  test.setTimeout(180_000)
  const suffix = `${Date.now()}`
  const custName = `Tr9 ${suffix}`
  const custCode = `T9${suffix}`
  const siteName = `Site ${suffix}`
  const siteCode = `S${suffix}`
  const termName = `Term ${suffix}`

  await page.goto('/customers')
  await expect(page.getByRole('heading', { name: 'Customers' })).toBeVisible()
  await page.getByTestId('customer-name').fill(custName)
  await page.getByTestId('customer-code').fill(custCode)
  await page.getByTestId('customer-create').click()
  await expect(page.getByRole('link', { name: custName })).toBeVisible({ timeout: 20_000 })

  await page.goto('/sites')
  await page.getByTestId('site-customer').selectOption({ label: new RegExp(custName) })
  await page.getByTestId('site-name').fill(siteName)
  await page.getByTestId('site-code').fill(siteCode)
  await page.getByTestId('site-create').click()
  await expect(page.getByText(siteCode).first()).toBeVisible({ timeout: 20_000 })

  await page.goto('/terminals/new')
  await page.getByTestId('terminal-create-site').selectOption({ label: new RegExp(siteName) })
  await page.getByTestId('terminal-create-name').fill(termName)
  await page.getByTestId('terminal-create-hex').fill(`T9${suffix}`.slice(0, 12))
  await page.getByTestId('terminal-create-submit').click()
  await expect(page).toHaveURL(/\/terminals\/[a-f0-9-]+$/i, { timeout: 30_000 })
  const terminalUrl = page.url()
  const terminalId = terminalUrl.split('/').pop()!

  await page.getByLabel(/Table set/i).selectOption({ index: 0 })
  await page.getByRole('button', { name: 'Assign set' }).click()
  await expect(page.getByText(/Active set:/)).toBeVisible({ timeout: 25_000 })

  await page.goto('/downloads')
  await page.getByLabel(/Terminal/i).selectOption(terminalId)
  await page.getByRole('button', { name: /Start download|Queue/i }).click()
  await expect(page.getByRole('heading', { name: 'Download batches' })).toBeVisible()

  await page.goto(`/dlog?terminal=${terminalId}`)
  await expect(page.getByRole('heading', { name: 'DLOG transactions' })).toBeVisible({ timeout: 20_000 })
  await expect(page.getByRole('link', { name: 'Detail' }).first()).toBeVisible({ timeout: 25_000 })
  await page.getByRole('link', { name: 'Detail' }).first().click()
  await expect(page.getByTestId('hex-viewer')).toBeVisible({ timeout: 20_000 })

  await page.goto('/customers')
  await page.getByRole('link', { name: custName }).click()
  await expect(page).toHaveURL(/\/customers\/[a-f0-9-]+$/i)
  const customerId = page.url().split('/').pop()!

  await page.goto('/rating-quote')
  await page.getByTestId('rating-quote-customer-id').fill(customerId!)
  await page.getByTestId('rating-quote-submit').click()
  await expect(page.locator('pre').filter({ hasText: /amountUsd|decisionKind/i }).first()).toBeVisible({
    timeout: 25_000,
  })

  await page.goto('/card-accounts')
  await page.getByTestId('card-account-terminal').fill(terminalId)
  await page.getByTestId('card-account-create').click()
  await expect(page.getByRole('heading', { name: 'Card accounts' })).toBeVisible({ timeout: 25_000 })

  await page.goto('/craft')
  await page.getByLabel(/Terminal/i).selectOption(terminalId)
  await page.getByTestId('craft-start-session').click()
  await expect(page.getByRole('heading', { name: /Craft session/i })).toBeVisible({ timeout: 25_000 })

  await page.goto('/firmware/jobs')
  await page.getByTestId('firmware-job-terminal').selectOption(terminalId)
  await page.getByTestId('firmware-job-start').click()
  await expect(page.getByRole('heading', { name: 'Firmware jobs' })).toBeVisible({ timeout: 25_000 })
})
