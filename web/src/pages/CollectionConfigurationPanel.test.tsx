import { cleanup, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, expect, it } from 'vitest'
import { CollectionConfigurationPanel, type CollectionConfiguration } from './CollectionConfigurationPanel'

afterEach(cleanup)
const configured: CollectionConfiguration = {
  statisticsRecordStatus: 'configured', callInRecordStatus: 'configured', statisticsIntervalDays: 1,
  statisticsUploadThreshold: 2, carrierStatisticsEnabled: true, optionalCdrMode: 'Completed calls only', optionalCdrModeMask: 4,
  optionalCdrUploadThreshold: 1, optionalWindowStart: '08:30', optionalWindowDays: 1, optionalWindowHours: 0,
  normalCdrUploadThreshold: 7, findings: [],
}

it('distinguishes the three thresholds and saved policy from terminal evidence', () => {
  render(<MemoryRouter><CollectionConfigurationPanel review={configured} setId="fixture" allowLinks /></MemoryRouter>)
  const panel = screen.getByRole('region', { name: 'Collection configuration review' })
  const value = (label: string) => within(panel).getByText(label).nextElementSibling
  expect(value('Statistics count threshold')).toHaveTextContent('2')
  expect(value('Optional CDR count threshold')).toHaveTextContent('1')
  expect(value('Normal CDR count threshold (outside optional window)')).toHaveTextContent('7')
  expect(value('Optional logging window (phone-local)')).toHaveTextContent('08:30, 1 day(s), 0 hour(s)')
  expect(within(panel).getByText(/not confirmation of what a phone has downloaded/)).toBeVisible()
  expect(within(panel).getByRole('link', { name: 'Review MT34 collection policy' })).toHaveAttribute('href','/table-sets/fixture?record=34#config-heading')
  expect(within(panel).getByRole('link', { name: 'Review MT35 normal call-in' })).toHaveAttribute('href','/table-sets/fixture?record=35#config-heading')
})

it('does not turn absent or unrecognized layouts into zero counts, or expose editor links without permission', () => {
  const review: CollectionConfiguration = {...configured, statisticsRecordStatus:'missing', callInRecordStatus:'unrecognized',
    statisticsIntervalDays:null, statisticsUploadThreshold:null, carrierStatisticsEnabled:null, optionalCdrMode:null, optionalCdrModeMask:null,
    optionalCdrUploadThreshold:null, optionalWindowStart:null, optionalWindowDays:null, optionalWindowHours:null, normalCdrUploadThreshold:null,
    findings:[{tableNumber:34,message:'MT34 is not included.'},{tableNumber:35,message:'Unknown layout.'}]}
  render(<MemoryRouter><CollectionConfigurationPanel review={review} setId="fixture" /></MemoryRouter>)
  expect(screen.getByText('Not included')).toBeVisible()
  expect(screen.getByText('Unrecognized layout')).toBeVisible()
  expect(screen.queryByText('Optional CDR count threshold')).toBeNull()
  expect(screen.queryByText('Normal CDR count threshold (outside optional window)')).toBeNull()
  expect(screen.queryByRole('link')).toBeNull()
  expect(screen.getByText(/MT34 is not included/)).toBeVisible()
})
