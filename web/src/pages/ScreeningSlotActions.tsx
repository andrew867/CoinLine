import { useState } from 'react'

export type ScreeningAction =
  | { slot: number; action: 'blank' | 'delete' }
  | { slot: number; action: 'speedDial'; shortcut: string; destination: string }

export function ScreeningSlotActions({ slot, action, description, editable, supportsSpeedDial, onAction, onDescription }: {
  slot: number
  action?: ScreeningAction
  description: string
  editable: boolean
  supportsSpeedDial: boolean
  onAction: (next?: ScreeningAction) => void
  onDescription: (next: string) => void
}) {
  const [shortcut, setShortcut] = useState(action?.action === 'speedDial' ? action.shortcut : '')
  const [destination, setDestination] = useState(action?.action === 'speedDial' ? action.destination : '')
  const [error, setError] = useState('')

  function stageSpeedDial() {
    if (!/^(\*[0-9]{1,2}|[2-9]11)$/.test(shortcut)) {
      setError('Enter * followed by one or two digits, or a service code from 211 through 911.')
      return
    }
    if (!/^[0-9]{4,}$/.test(destination) || shortcut.length + destination.length + 1 > 16) {
      setError('Enter a full destination number that fits in the 16-digit pattern.')
      return
    }
    setError('')
    onAction({ slot, action: 'speedDial', shortcut, destination })
  }

  return <>
    {action?.action !== 'blank' && action?.action !== 'delete' && <label className="native-table-field">
      <span>Description</span>
      <input value={description} maxLength={120} onChange={event => onDescription(event.target.value)} />
    </label>}
    {editable && <div className="row-actions">
      {action ? <button type="button" className="quiet" onClick={() => onAction(undefined)}>Undo screening action</button>
        : <>
          <button type="button" className="quiet" onClick={() => onAction({ slot, action: 'blank' })}>Blank entry</button>
          <button type="button" className="quiet" onClick={() => onAction({ slot, action: 'delete' })}>Delete entry</button>
        </>}
      {action && <span role="status">
        {action.action === 'speedDial' ? `Staged speed dial ${action.shortcut}*${action.destination}.`
          : action.action === 'blank' ? 'Number, description and wildcard count will be cleared.'
            : 'The entry settings will be reset and its slot retained.'}
      </span>}
    </div>}
    {editable && supportsSpeedDial && !action && <details>
      <summary>Set speed dial</summary>
      <p>For manual dialing, keep a regular screening entry for the destination with matching call settings.</p>
      <div className="table-config-selectors">
        <label>Shortcut<input value={shortcut} maxLength={3} placeholder="*45" onChange={event => setShortcut(event.target.value)} /></label>
        <label>Destination number<input value={destination} maxLength={13} inputMode="numeric"
          onChange={event => setDestination(event.target.value)} /></label>
        <button type="button" onClick={stageSpeedDial}>Stage speed dial</button>
      </div>
      {error && <p role="alert">{error}</p>}
    </details>}
  </>
}
