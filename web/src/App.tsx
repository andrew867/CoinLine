import { BrowserRouter, Route, Routes } from 'react-router-dom'
import { Layout } from './components/Layout'
import { AuditEventsPage } from './pages/AuditEventsPage'
import { CustomerDetail } from './pages/CustomerDetail'
import { CustomersPage } from './pages/CustomersPage'
import { Dashboard } from './pages/Dashboard'
import { DownloadDetail } from './pages/DownloadDetail'
import { DownloadsPage } from './pages/DownloadsPage'
import { DlogReplayDebug } from './pages/DlogReplayDebug'
import { DlogTransactionDetail } from './pages/DlogTransactionDetail'
import { DlogTransactions } from './pages/DlogTransactions'
import { NccFrameCaptures } from './pages/NccFrameCaptures'
import { TableDefinitionDetail } from './pages/TableDefinitionDetail'
import { TableDefinitions } from './pages/TableDefinitions'
import { TableSetDetail } from './pages/TableSetDetail'
import { TableSets } from './pages/TableSets'
import { TableVersions } from './pages/TableVersions'
import { SitesPage } from './pages/SitesPage'
import { TerminalCreatePage } from './pages/TerminalCreatePage'
import { TerminalDetail } from './pages/TerminalDetail'
import { TerminalsList } from './pages/TerminalsList'
import { RatePlansPage } from './pages/RatePlansPage'
import { RatePlanDetail } from './pages/RatePlanDetail'
import { NumberClassesPage } from './pages/NumberClassesPage'
import { RatingQuoteTool } from './pages/RatingQuoteTool'
import { CallRecordsPage } from './pages/CallRecordsPage'
import { CallRecordDetail } from './pages/CallRecordDetail'
import { CardProductsPage } from './pages/CardProductsPage'
import { CardProductDetail } from './pages/CardProductDetail'
import { CardAccountsPage } from './pages/CardAccountsPage'
import { CardAccountDetail } from './pages/CardAccountDetail'
import { PaymentTransactionsPage } from './pages/PaymentTransactionsPage'
import { SmartcardTypesPage } from './pages/SmartcardTypesPage'
import { ReconciliationBatchesPage } from './pages/ReconciliationBatchesPage'
import { ReconciliationBatchDetail } from './pages/ReconciliationBatchDetail'
import { CraftSessionsPage } from './pages/CraftSessionsPage'
import { CraftSessionDetail } from './pages/CraftSessionDetail'
import { CraftCommandDetail } from './pages/CraftCommandDetail'
import { FirmwareJobDetail } from './pages/FirmwareJobDetail'
import { FirmwareJobsPage } from './pages/FirmwareJobsPage'
import { FirmwarePackageDetail } from './pages/FirmwarePackageDetail'
import { FirmwarePackagesPage } from './pages/FirmwarePackagesPage'
import { FirmwareTargetsPage } from './pages/FirmwareTargetsPage'
import { FirmwareVersionsPage } from './pages/FirmwareVersionsPage'
import { NccSessionsPage } from './pages/NccSessionsPage'
import { NotFoundPage } from './pages/NotFoundPage'
import { StatusPage } from './pages/StatusPage'
import { UploadDetail } from './pages/UploadDetail'
import { UploadsPage } from './pages/UploadsPage'

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route path="/" element={<Dashboard />} />
          <Route path="/customers/:id" element={<CustomerDetail />} />
          <Route path="/customers" element={<CustomersPage />} />
          <Route path="/sites" element={<SitesPage />} />
          <Route path="/terminals/new" element={<TerminalCreatePage />} />
          <Route path="/terminals/:id" element={<TerminalDetail />} />
          <Route path="/terminals" element={<TerminalsList />} />
          <Route path="/ncc-sessions" element={<NccSessionsPage />} />
          <Route path="/ncc-frame-captures" element={<NccFrameCaptures />} />
          <Route path="/dlog/replay-debug" element={<DlogReplayDebug />} />
          <Route path="/dlog/:id" element={<DlogTransactionDetail />} />
          <Route path="/dlog" element={<DlogTransactions />} />
          <Route path="/table-definitions/:id" element={<TableDefinitionDetail />} />
          <Route path="/table-definitions" element={<TableDefinitions />} />
          <Route path="/table-versions" element={<TableVersions />} />
          <Route path="/table-sets/:id" element={<TableSetDetail />} />
          <Route path="/table-sets" element={<TableSets />} />
          <Route path="/downloads/:id" element={<DownloadDetail />} />
          <Route path="/downloads" element={<DownloadsPage />} />
          <Route path="/uploads/:id" element={<UploadDetail />} />
          <Route path="/uploads" element={<UploadsPage />} />
          <Route path="/rate-plans/:id" element={<RatePlanDetail />} />
          <Route path="/rate-plans" element={<RatePlansPage />} />
          <Route path="/number-classes" element={<NumberClassesPage />} />
          <Route path="/rating-quote" element={<RatingQuoteTool />} />
          <Route path="/call-records/:id" element={<CallRecordDetail />} />
          <Route path="/call-records" element={<CallRecordsPage />} />
          <Route path="/card-products/:id" element={<CardProductDetail />} />
          <Route path="/card-products" element={<CardProductsPage />} />
          <Route path="/card-accounts/:id" element={<CardAccountDetail />} />
          <Route path="/card-accounts" element={<CardAccountsPage />} />
          <Route path="/payment-transactions" element={<PaymentTransactionsPage />} />
          <Route path="/smartcard-types" element={<SmartcardTypesPage />} />
          <Route path="/card-reconciliation/:id" element={<ReconciliationBatchDetail />} />
          <Route path="/card-reconciliation" element={<ReconciliationBatchesPage />} />
          <Route path="/craft/commands/:commandId" element={<CraftCommandDetail />} />
          <Route path="/craft/:sessionId" element={<CraftSessionDetail />} />
          <Route path="/craft" element={<CraftSessionsPage />} />
          <Route path="/firmware/packages/:id" element={<FirmwarePackageDetail />} />
          <Route path="/firmware/packages" element={<FirmwarePackagesPage />} />
          <Route path="/firmware/targets" element={<FirmwareTargetsPage />} />
          <Route path="/firmware/versions" element={<FirmwareVersionsPage />} />
          <Route path="/firmware/jobs/:id" element={<FirmwareJobDetail />} />
          <Route path="/firmware/jobs" element={<FirmwareJobsPage />} />
          <Route path="/audit" element={<AuditEventsPage />} />
          <Route path="/status" element={<StatusPage />} />
          <Route
            path="/settings"
            element={
              <div>
                <h1>Settings</h1>
                <p>
                  API base uses the Vite dev proxy to <code>http://localhost:5006</code>. Production uses{' '}
                  <code>X-API-Key</code> when <code>Security:Mode=ApiKey</code> — see{' '}
                  <code>docs/configuration/secrets-and-config.md</code>.
                </p>
              </div>
            }
          />
          <Route path="*" element={<NotFoundPage />} />
        </Route>
      </Routes>
    </BrowserRouter>
  )
}
