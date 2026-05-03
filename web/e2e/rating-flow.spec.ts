import { test, expect } from '@playwright/test'

test('rate plans and quote pages render with MVP warnings', async ({ page }) => {
  await page.goto('/rate-plans')
  await expect(page.getByRole('heading', { name: 'Rate plans' })).toBeVisible()
  await expect(page.getByText(/not production parity/i)).toBeVisible()

  await page.goto('/rating-quote')
  await expect(page.getByRole('heading', { name: 'Rating quote (test)' })).toBeVisible()
  await expect(page.getByText(/lab validation/i)).toBeVisible()
})

test('number classes page lists routing flags in copy or table', async ({ page }) => {
  await page.goto('/number-classes')
  await expect(page.getByRole('heading', { name: 'Number classes' })).toBeVisible()
  await expect(page.getByText(/blocked.*free.*emergency/i)).toBeVisible()
})
