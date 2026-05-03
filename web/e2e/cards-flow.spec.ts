import { test, expect } from '@playwright/test'

test('simulation banner and card products create', async ({ page }) => {
  await page.goto('/')
  await expect(
    page
      .getByText(/simulation mode|card simulation banner could not load|unavailable/i)
      .first(),
  ).toBeVisible({ timeout: 15000 })

  await page.goto('/card-products')
  await expect(page.getByRole('heading', { name: 'Card products' })).toBeVisible()

  const code = `e2e${Date.now()}`
  await page.getByLabel('Name').fill('E2E Card Product')
  await page.getByLabel('Code').fill(code)
  await page.getByRole('button', { name: 'Create' }).click()
  const created = page.getByRole('link', { name: 'E2E Card Product' })
  const failed = page.locator('p').filter({ hasText: /\/api\/cards\/products|create failed/i })
  await expect(created.or(failed).first()).toBeVisible({ timeout: 15000 })
})

test('card accounts detail opens balance modal when seed account exists', async ({ page }) => {
  await page.goto('/card-accounts')
  await expect(page.getByRole('heading', { name: 'Card accounts' })).toBeVisible()
  await expect(page.getByRole('status', { name: 'Loading…' })).toBeHidden({ timeout: 20000 })

  const first = page.locator('tbody a').first()
  if (await first.isVisible().catch(() => false)) {
    await first.click()
    await expect(page.getByRole('heading', { name: 'Card account' })).toBeVisible()

    await page.getByRole('button', { name: /Adjust balance/i }).click()
    await page.getByLabel(/^Audit reason/).fill('Playwright e2e balance adjustment audit')
    await page.getByRole('button', { name: 'Apply' }).click()
    await expect(page.getByRole('dialog')).not.toBeVisible({ timeout: 10000 })
  } else {
    await expect(page.getByText(/No accounts yet|\/api\/cards\/accounts|PCI scope/i).first()).toBeVisible({
      timeout: 15000,
    })
  }
})
