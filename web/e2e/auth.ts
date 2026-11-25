import { expect, type Page } from '@playwright/test'

export async function signIn(page: Page) {
  await page.goto('/login')
  await page.getByLabel('Username').fill('operator')
  await page.getByLabel('Password').fill('lab-operator')
  await page.getByRole('button', { name: 'Sign in' }).click()
  await expect(page.getByRole('heading', { name: 'Home' })).toBeVisible({ timeout: 15_000 })
}
