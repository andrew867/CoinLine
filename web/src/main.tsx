import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'

const originalFetch = window.fetch.bind(window)
window.fetch = (input, init) => {
  const headers = new Headers(init?.headers)
  headers.delete('X-Operator-Id')
  headers.delete('X-Operator-Role')
  const token = localStorage.getItem('coinline.token')
  if (token) headers.set('Authorization', `Bearer ${token}`)
  const apiKey = localStorage.getItem('coinline.apiKey')
  if (apiKey) headers.set('X-API-Key', apiKey)
  return originalFetch(input, { ...init, headers })
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
