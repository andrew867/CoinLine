import { test, expect } from '@playwright/test'

test('dashboard loads', async ({ page }) => {
  await page.goto('/')
  await expect(page.getByRole('heading', { name: 'Dashboard' })).toBeVisible()
})

test('customers page renders (JSON requires API on :5006)', async ({ page }) => {
  await page.goto('/customers')
  await expect(page.getByRole('heading', { name: 'Customers' })).toBeVisible()
  await expect(
    page.getByText('Sample Transit Co').or(page.getByText('Loading…')).or(page.locator('[style*="crimson"]')),
  ).toBeVisible({ timeout: 15_000 })
})

test('ncc frame captures page loads', async ({ page }) => {
  await page.goto('/ncc-frame-captures')
  await expect(page.getByRole('heading', { name: 'NCC frame captures' })).toBeVisible()
})

test('dlog transactions page loads', async ({ page }) => {
  await page.goto('/dlog')
  await expect(page.getByRole('heading', { name: 'DLOG transactions' })).toBeVisible()
})

test('dlog replay debug page loads', async ({ page }) => {
  await page.goto('/dlog/replay-debug')
  await expect(page.getByRole('heading', { name: 'DLOG replay (debug)' })).toBeVisible()
})

test('table distribution pages render (full flow needs API on :5006)', async ({ page }) => {
  for (const path of ['/table-definitions', '/table-versions', '/table-sets', '/downloads']) {
    await page.goto(path)
    await expect(page.locator('h1').first()).toBeVisible()
  }
})
