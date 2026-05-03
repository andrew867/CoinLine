import { test, expect } from '@playwright/test'

test('firmware packages page and live-flash banner', async ({ page }) => {
  await page.goto('/firmware/packages')
  await expect(page.getByRole('heading', { name: 'Firmware packages' })).toBeVisible()
  await expect(page.getByRole('alert').filter({ hasText: /Live firmware update disabled/i })).toBeVisible({
    timeout: 15_000,
  })
})

test('firmware job detail can run simulation when API available', async ({ page }) => {
  await page.goto('/firmware/jobs')
  await expect(page.getByRole('heading', { name: 'Firmware jobs' })).toBeVisible()
  const link = page.locator('tbody tr a').first()
  if (await link.isVisible().catch(() => false)) {
    await link.click()
    await expect(page.getByRole('heading', { name: 'Firmware job' })).toBeVisible({ timeout: 15_000 })
    await page.getByRole('button', { name: 'Run simulation' }).click()
    await expect(page.getByText(/dla_xmodem_transport|Simulation completed/i)).toBeVisible({ timeout: 20_000 })
  } else {
    await expect(page.getByText(/Firmware jobs|Loading/i).first()).toBeVisible()
  }
})
