import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router'
import { authApi, type Me } from './api/auth'
import { StepUpProvider } from './auth/StepUpProvider'
import { HomePage, SessionsPage } from './pages/AccountPages'
import {
  ConfirmEmailPage,
  ForgotPasswordPage,
  LoginPage,
  MfaEnrolPage,
  MfaVerifyPage,
  RegisterPage,
  ResetPasswordPage,
} from './pages/AuthPages'

/** Pages behind sign-in with completed MFA; anyone else goes to the login page. */
function RequireSession({ children }: { children: (me: Me) => ReactNode }) {
  const me = useQuery({ queryKey: ['me'], queryFn: authApi.me, retry: false })
  if (me.isPending) {
    return <p className="p-8 text-sm">Učitavanje...</p>
  }
  return me.data ? children(me.data) : <Navigate to="/login" replace />
}

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route path="/confirm-email" element={<ConfirmEmailPage />} />
      <Route path="/forgot-password" element={<ForgotPasswordPage />} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />
      <Route path="/mfa/enrol" element={<MfaEnrolPage />} />
      <Route path="/mfa/verify" element={<MfaVerifyPage />} />
      <Route path="/sessions" element={<RequireSession>{() => <SessionsPage />}</RequireSession>} />
      <Route
        path="/"
        element={<RequireSession>{(me) => <HomePage email={me.email} />}</RequireSession>}
      />
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  )
}

function App() {
  const [queryClient] = useState(
    () => new QueryClient({ defaultOptions: { queries: { retry: false } } }),
  )
  return (
    <QueryClientProvider client={queryClient}>
      <StepUpProvider>
        <BrowserRouter>
          <AppRoutes />
        </BrowserRouter>
      </StepUpProvider>
    </QueryClientProvider>
  )
}

export default App
