import { useSyncExternalStore } from 'react'
import { Navigate, Outlet } from 'react-router-dom'
import { consoleSession } from '../api/consoleSession'

export function RequireSignIn() {
  const token = useSyncExternalStore(consoleSession.subscribe, consoleSession.token)
  if (!token) return <Navigate to="/login" replace state={{ sessionExpired: consoleSession.wasExpired() }} />
  return <Outlet />
}
