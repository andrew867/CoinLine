import { test, expect } from '@playwright/test'

/**
 * Tranche 11: NCC sessions list is a first-class operator page (not raw JSON).
 * With API + demo seed, at least one session may appear with a terminal link.
 */
test('ncc sessions table or empty state when API available', async ({ page }) => {
  test.setTimeout(60_000)
  await page.goto('/ncc-sessions')
  await expect(page.getByRole('heading', { name: 'NCC sessions' })).toBeVisible()

  await expect(page.getByText('Loading…')).toBeHidden({ timeout: 30_000 })

  if (await page.getByTestId('ncc-sessions-error').isVisible()) {
    return
  }

  await expect(page.getByTestId('ncc-sessions-summary')).toBeVisible()
  await expect(
    page
      .getByText('No NCC sessions in this view.')
      .or(page.getByText('No NCC sessions yet.'))
      .or(page.getByTestId('ncc-sessions-table')),
  ).toBeVisible()

  if (await page.getByTestId('ncc-sessions-table').isVisible()) {
    await expect(page.getByRole('columnheader', { name: 'Correlation' })).toBeVisible()
    const termLink = page.getByTestId('ncc-session-terminal-link').first()
    if (await termLink.isVisible()) {
      await expect(termLink).toHaveAttribute('href', /\/terminals\/[a-f0-9-]+$/i)
    }
  }
})
