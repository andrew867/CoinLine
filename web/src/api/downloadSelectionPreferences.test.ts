import { afterEach, expect, it } from 'vitest'
import { readDownloadSelectionPreferences, saveDownloadSelectionPreference } from './downloadSelectionPreferences'

const key = 'coinline.downloadSelectionPreferences.v1'
afterEach(() => localStorage.removeItem(key))

it('persists independent sorted standard and inmate choices and clears each separately', () => {
  expect(readDownloadSelectionPreferences()).toEqual({ standard: null, inmate: null })
  saveDownloadSelectionPreference('standard', [73, 29])
  saveDownloadSelectionPreference('inmate', [26])
  expect(readDownloadSelectionPreferences()).toEqual({ standard: [29, 73], inmate: [26] })
  saveDownloadSelectionPreference('standard', null)
  expect(readDownloadSelectionPreferences()).toEqual({ standard: null, inmate: [26] })
  saveDownloadSelectionPreference('inmate', null)
  expect(localStorage.getItem(key)).toBeNull()
})

it('rejects malformed preferences and preserves valid independent choices', () => {
  localStorage.setItem(key, '{malformed')
  expect(readDownloadSelectionPreferences()).toEqual({ standard: null, inmate: null })
  localStorage.setItem(key, JSON.stringify({ standard: [29, 29], inmate: [34] }))
  expect(readDownloadSelectionPreferences()).toEqual({ standard: null, inmate: [34] })
  for (const numbers of [[], [0], [256], [29, 29], [3.5]])
    expect(() => saveDownloadSelectionPreference('standard', numbers)).toThrow()
  expect(readDownloadSelectionPreferences()).toEqual({ standard: null, inmate: [34] })
})
