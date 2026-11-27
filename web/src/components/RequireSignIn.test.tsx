import { act, cleanup, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, expect, it, vi } from 'vitest'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'
import { readDownloadReviews, rememberDownloadReview } from '../api/downloadReviews'
import { LoginPage } from '../pages/LoginPage'
import { RequireSignIn } from './RequireSignIn'

afterEach(() => { cleanup(); consoleSession.clear(); vi.unstubAllGlobals() })

function mountProtectedRoute() {
  render(<MemoryRouter initialEntries={['/protected']}><Routes>
    <Route path="/login" element={<LoginPage />} />
    <Route element={<RequireSignIn />}><Route path="/protected" element={<h1>Protected console</h1>} /></Route>
  </Routes></MemoryRouter>)
}

it('unmounts the protected page and displays the login form when a current API token expires', async () => {
  consoleSession.save('fixture-token','fixture-api-key','Fixture operator','Operator',{phones:1})
  vi.stubGlobal('fetch',vi.fn().mockResolvedValue(new Response(null,{status:401})))
  mountProtectedRoute()
  expect(screen.getByRole('heading',{name:'Protected console'})).toBeVisible()
  await act(async () => { await apiFetch('/api/terminals') })
  expect(await screen.findByRole('heading',{name:'CoinLine'})).toBeVisible()
  expect(screen.getByRole('status')).toHaveTextContent('Your session expired. Sign in again.')
  expect(screen.queryByRole('heading',{name:'Protected console'})).toBeNull()
  expect(screen.getByLabelText('API key')).toHaveValue('')
})

it('redirects another tab signed out through storage without claiming a timeout', async () => {
  consoleSession.save('fixture-token','','Fixture operator','Operator')
  rememberDownloadReview({terminalId:'fixture',tableSetId:null,reason:'registration'})
  mountProtectedRoute()
  await act(async () => {
    localStorage.removeItem('coinline.token')
    window.dispatchEvent(new StorageEvent('storage',{key:'coinline.token',oldValue:'fixture-token',newValue:null,storageArea:localStorage}))
  })
  await waitFor(() => expect(screen.getByRole('heading',{name:'CoinLine'})).toBeVisible())
  expect(screen.queryByText('Your session expired. Sign in again.')).toBeNull()
  expect(readDownloadReviews()).toEqual([])
})
