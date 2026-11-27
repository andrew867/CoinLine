import { useEffect, useRef, useState } from 'react'
import { apiFetch } from '../api/client'
import { consoleSession } from '../api/consoleSession'

export function CraftAccessReveal({ endpoint, revision, sha256, profile }: {
  endpoint: string; revision: number; sha256: string; profile: string
}) {
  const [code, setCode] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const request = useRef<AbortController | null>(null)
  const timer = useRef<number | null>(null)

  function hide() {
    request.current?.abort(); request.current = null
    if (timer.current !== null) window.clearTimeout(timer.current)
    timer.current = null
    setCode(null); setMessage(''); setBusy(false)
  }

  useEffect(() => {
    const onVisibility = () => { if (document.hidden) hide() }
    const onStorage = () => hide()
    window.addEventListener('blur', hide)
    window.addEventListener('storage', onStorage)
    document.addEventListener('visibilitychange', onVisibility)
    return () => {
      request.current?.abort()
      if (timer.current !== null) window.clearTimeout(timer.current)
      window.removeEventListener('blur', hide)
      window.removeEventListener('storage', onStorage)
      document.removeEventListener('visibilitychange', onVisibility)
    }
  }, [])

  async function reveal() {
    hide()
    const controller = new AbortController()
    request.current = controller
    const token = consoleSession.token()
    setBusy(true)
    try {
      const response = await apiFetch(`${endpoint}/craft-access`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' }, cache: 'no-store', signal: controller.signal,
        body: JSON.stringify({ revision, sha256, profile }),
      })
      if (controller.signal.aborted || token !== consoleSession.token() || consoleSession.role() !== 'Admin') return
      if (!response.ok) {
        setMessage(response.status === 409 ? 'The record changed. Reopen the editor.' : 'The configured code could not be shown.')
        return
      }
      const result = await response.json() as { code?: string | null }
      if (controller.signal.aborted || token !== consoleSession.token() || consoleSession.role() !== 'Admin') return
      if (typeof result.code !== 'string' || !/^\d{7}$/.test(result.code)) {
        setMessage('The stored code is not in the recognized format. Set a replacement on a draft if appropriate.')
        return
      }
      setCode(result.code)
      timer.current = window.setTimeout(hide, 30_000)
    } catch {
      if (!controller.signal.aborted) setMessage('The configured code could not be shown.')
    } finally { if (!controller.signal.aborted) setBusy(false) }
  }

  return <section aria-label="Configured Craft access code" className="native-table-credential">
    <p>The saved code is separate from the replacement field below. Showing it requires administrator access and is recorded in the audit log.</p>
    {code === null ? <button type="button" className="quiet" disabled={busy} onClick={() => void reveal()}>
      {busy ? 'Loading…' : 'Show configured access code'}
    </button> : <div className="row-actions"><code aria-label="Saved Craft access code">{code}</code>
      <button type="button" className="quiet" onClick={hide}>Hide access code</button></div>}
    {message && <p role="status">{message}</p>}
  </section>
}
