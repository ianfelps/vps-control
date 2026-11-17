import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createBrowserRouter, Link, RouterProvider } from 'react-router'
import { ApiError } from '@/api/client'
import { AppLayout } from '@/components/layout/AppLayout'
import { RequireAuth } from '@/components/RequireAuth'
import { ApplicationDetailPage } from '@/pages/applications/ApplicationDetailPage'
import { ApplicationsPage } from '@/pages/applications/ApplicationsPage'
import { AuditPage } from '@/pages/AuditPage'
import { ContainerDetailPage } from '@/pages/containers/ContainerDetailPage'
import { ContainersPage } from '@/pages/containers/ContainersPage'
import { DashboardPage } from '@/pages/dashboard/DashboardPage'
import { LoginPage } from '@/pages/LoginPage'
import './index.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: (count, error) => !(error instanceof ApiError && [401, 403].includes(error.status)) && count < 2,
      refetchOnWindowFocus: false,
    },
  },
})

const router = createBrowserRouter([
  { path: '/login', element: <LoginPage /> },
  {
    element: <RequireAuth />,
    children: [
      {
        element: <AppLayout />,
        children: [
          { index: true, element: <DashboardPage /> },
          { path: 'containers', element: <ContainersPage /> },
          { path: 'containers/:id', element: <ContainerDetailPage /> },
          { path: 'applications', element: <ApplicationsPage /> },
          { path: 'applications/:slug', element: <ApplicationDetailPage /> },
          { path: 'audit', element: <AuditPage /> },
          {
            path: '*',
            element: (
              <div className="space-y-2 py-10 text-center">
                <p className="text-lg font-medium">Página não encontrada</p>
                <Link to="/" className="text-sm text-primary hover:underline">
                  Voltar ao dashboard
                </Link>
              </div>
            ),
          },
        ],
      },
    ],
  },
])

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
)
