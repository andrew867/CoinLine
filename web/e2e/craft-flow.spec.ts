import { test, expect } from '@playwright/test'

test('craft sessions page loads and can open new session flow', async ({ page }) => {
  await page.goto('/craft')
  await expect(page.getByRole('heading', { name: 'Craft sessions' })).toBeVisible()

  await page.getByRole('button', { name: 'Start craft session' }).click()
  await expect(page.getByRole('heading', { name: 'Craft session' })).toBeVisible({ timeout: 15000 })

  await expect(page.getByLabel(/Technician id/i)).toBeVisible()
  await page.locator('#craft-command-name').fill('ping')
  await page.locator('#craft-request-hex').fill('00')

  await page.getByRole('button', { name: 'Enqueue command' }).click()
  await expect(page.getByRole('heading', { name: 'Command status' })).toBeVisible({ timeout: 15000 })
})

test('terminal detail shows field diagnostics controls', async ({ page }) => {
  await page.goto('/terminals')
  const link = page.locator('tbody a').first()
  if (await link.isVisible().catch(() => false)) {
    await link.click()
    await expect(page.getByRole('heading', { name: /Field diagnostics/i })).toBeVisible()
    await expect(page.getByRole('button', { name: /Save diagnostic snapshot/i })).toBeVisible()
    await expect(page.getByRole('button', { name: /Request CDR upload/i })).toBeVisible()
  } else {
    await expect(page.getByText(/No terminals|terminals/i).first()).toBeVisible()
  }
})
