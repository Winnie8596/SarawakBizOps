import { Navigate, Route, Routes } from 'react-router-dom'
import { AuthProvider, useAuth } from './auth/AuthContext'
import { ProtectedRoute } from './auth/ProtectedRoute'
import { INVENTORY_ROLES, OFFICE_ROLES } from './auth/roles'
import { AppLayout } from './layouts/AppLayout'
import { TechnicianLayout } from './layouts/TechnicianLayout'
import { LoginPage } from './pages/LoginPage'
import { DashboardPage } from './pages/DashboardPage'
import { CustomersPage } from './pages/CustomersPage'
import { CustomerDetailPage } from './pages/CustomerDetailPage'
import { EquipmentPage } from './pages/EquipmentPage'
import { EquipmentDetailPage } from './pages/EquipmentDetailPage'
import { ServiceRequestsPage } from './pages/ServiceRequestsPage'
import { ServiceRequestDetailPage } from './pages/ServiceRequestDetailPage'
import { WorkOrdersPage } from './pages/WorkOrdersPage'
import { WorkOrderDetailPage } from './pages/WorkOrderDetailPage'
import { TechnicianJobsPage } from './pages/tech/TechnicianJobsPage'
import { TechnicianJobDetailPage } from './pages/tech/TechnicianJobDetailPage'
import { InventoryPage } from './pages/InventoryPage'
import { InventoryHistoryPage } from './pages/InventoryHistoryPage'
import { UsersPage } from './pages/UsersPage'
import { ChangePasswordPage } from './pages/ChangePasswordPage'
import { NotFoundPage } from './pages/NotFoundPage'

// A Technician has no use for the office dashboard (PRD §6.8): send them straight to their own
// mobile-first job list instead, chosen automatically the moment they land on '/'.
function HomeRedirect() {
  const { hasRole } = useAuth()
  return hasRole('Technician') ? <Navigate to="/tech" replace /> : <DashboardPage />
}

export default function App() {
  return (
    <AuthProvider>
      <Routes>
        <Route path="/login" element={<LoginPage />} />

        <Route element={<ProtectedRoute />}>
          <Route element={<ProtectedRoute roles={['Technician']} />}>
            <Route path="/tech" element={<TechnicianLayout />}>
              <Route index element={<Navigate to="jobs" replace />} />
              <Route path="jobs" element={<TechnicianJobsPage />} />
              <Route path="jobs/:id" element={<TechnicianJobDetailPage />} />
            </Route>
          </Route>

          <Route element={<AppLayout />}>
            <Route path="/" element={<HomeRedirect />} />
            <Route path="/account/password" element={<ChangePasswordPage />} />

            <Route element={<ProtectedRoute roles={[...OFFICE_ROLES]} />}>
              <Route path="/customers" element={<CustomersPage />} />
              <Route path="/customers/:id" element={<CustomerDetailPage />} />
              <Route path="/equipment" element={<EquipmentPage />} />
              <Route path="/equipment/:id" element={<EquipmentDetailPage />} />
              <Route path="/service-requests" element={<ServiceRequestsPage />} />
              <Route path="/service-requests/:id" element={<ServiceRequestDetailPage />} />
              <Route path="/work-orders" element={<WorkOrdersPage />} />
              <Route path="/work-orders/:id" element={<WorkOrderDetailPage />} />
            </Route>

            <Route element={<ProtectedRoute roles={[...INVENTORY_ROLES]} />}>
              <Route path="/inventory" element={<InventoryPage />} />
              <Route path="/inventory/history" element={<InventoryHistoryPage />} />
            </Route>

            <Route element={<ProtectedRoute roles={['Admin']} />}>
              <Route path="/users" element={<UsersPage />} />
            </Route>
          </Route>
        </Route>

        <Route path="/404" element={<NotFoundPage />} />
        <Route path="*" element={<Navigate to="/404" replace />} />
      </Routes>
    </AuthProvider>
  )
}
