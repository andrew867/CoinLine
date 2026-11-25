import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, expect, it } from 'vitest'
import { FieldPreferencesPanel } from './FieldPreferencesPanel'
import { applyFieldPreferences, defaultFieldPreferences, readFieldPreferences } from '../api/fieldPreferences'
import { consoleSession } from '../api/consoleSession'

afterEach(() => {
  cleanup()
  localStorage.removeItem('coinline.fieldPreferences')
  applyFieldPreferences(defaultFieldPreferences)
  consoleSession.clear()
})

it('saves independent text and background highlights and preserves workstation settings on sign-out', () => {
  render(<FieldPreferencesPanel />)
  expect(screen.getByLabelText('Key fields text color')).toBeDisabled()
  fireEvent.click(screen.getByLabelText('Highlight key fields'))
  fireEvent.change(screen.getByLabelText('Key fields text color'), { target: { value: '#123456' } })
  fireEvent.change(screen.getByLabelText('Key fields background color'), { target: { value: '#abcdef' } })
  fireEvent.click(screen.getByLabelText('Highlight mandatory fields'))
  fireEvent.change(screen.getByLabelText('Mandatory fields background color'), { target: { value: '#ffccdd' } })

  const root = document.documentElement
  expect(root.getAttribute('data-highlight-key')).not.toBe('true')
  fireEvent.click(screen.getByRole('button', { name: 'Save field highlighting' }))
  expect(screen.getByRole('status')).toHaveTextContent('saved')
  expect(root.getAttribute('data-highlight-key')).toBe('true')
  expect(root.style.getPropertyValue('--field-key-text')).toBe('#123456')
  expect(root.style.getPropertyValue('--field-key-background')).toBe('#abcdef')
  expect(root.getAttribute('data-highlight-mandatory')).toBe('true')
  expect(root.style.getPropertyValue('--field-mandatory-background')).toBe('#ffccdd')

  consoleSession.save('fixture-token', 'fixture-key')
  consoleSession.clear()
  expect(readFieldPreferences().key).toEqual({ enabled: true, text: '#123456', background: '#abcdef' })
  expect(readFieldPreferences().mandatory.background).toBe('#ffccdd')
  cleanup()
  render(<FieldPreferencesPanel />)
  expect(screen.getByLabelText('Highlight key fields')).toBeChecked()
  expect(screen.getByLabelText('Key fields text color')).toHaveValue('#123456')
  fireEvent.click(screen.getByRole('button', { name: 'Restore defaults' }))
  expect(root.getAttribute('data-highlight-key')).toBe('false')
  expect(root.getAttribute('data-highlight-mandatory')).toBe('false')
  expect(readFieldPreferences()).toEqual(defaultFieldPreferences)
})

it('does not apply malformed or invalid stored color values', () => {
  localStorage.setItem('coinline.fieldPreferences', '{invalid')
  expect(readFieldPreferences()).toEqual(defaultFieldPreferences)
  localStorage.setItem('coinline.fieldPreferences', JSON.stringify({
    key: { enabled: true, text: 'invalid color', background: '#000000' },
    mandatory: { enabled: 'true', background: null },
  }))
  expect(readFieldPreferences()).toEqual({
    key: { ...defaultFieldPreferences.key, enabled: true, background: '#000000' },
    mandatory: defaultFieldPreferences.mandatory,
  })
})
