import { Navigate, Route, Routes } from 'react-router-dom'
import { AuthProvider } from './auth/AuthContext'
import { ProtectedRoute } from './auth/ProtectedRoute'
import { OFFICE_ROLES } from './auth/roles'
import { AppLayout } from './layouts/AppLayout'
import { LoginPage } from './pages/LoginPage'
import { DashboardPage } from './pages/DashboardPage'
import { CustomersPage } from './pages/CustomersPage'
import { CustomerDetailPage } from './pages/CustomerDetailPage'
import { EquipmentPage } from './pages/EquipmentPage'
import { EquipmentDetailPage } from './pages/EquipmentDetailPage'
import { ServiceRequestsPage } from './pages/ServiceRequestsPage'
import { ServiceRequestDetailPage } from './pages/ServiceRequestDetailPage'
import { UsersPage } from './pages/UsersPage'
import { ChangePasswordPage } from './pages/ChangePasswordPage'
import { NotFoundPage } from './pages/NotFoundPage'

export default function App() {
  return (
    <AuthProvider>
      <Routes>
        <Route path="/login" element={<LoginPage />} />

        <Route element={<ProtectedRoute />}>
          <Route element={<AppLayout />}>
            <Route path="/" element={<DashboardPage />} />
            <Route path="/account/password" element={<ChangePasswordPage />} />

            <Route element={<ProtectedRoute roles={[...OFFICE_ROLES]} />}>
              <Route path="/customers" element={<CustomersPage />} />
              <Route path="/customers/:id" element={<CustomerDetailPage />} />
              <Route path="/equipment" element={<EquipmentPage />} />
              <Route path="/equipment/:id" element={<EquipmentDetailPage />} />
              <Route path="/service-requests" element={<ServiceRequestsPage />} />
              <Route path="/service-requests/:id" element={<ServiceRequestDetailPage />} />
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
