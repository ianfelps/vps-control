import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Boxes, Server } from 'lucide-react'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createBrowserRouter, Link, RouterProvider } from 'react-router'
import { ApiError } from '@/api/client'
import { AppLayout } from '@/components/layout/AppLayout'
import { RequireAuth } from '@/components/RequireAuth'
import { AuditPage } from '@/pages/AuditPage'
import { DashboardPage } from '@/pages/dashboard/DashboardPage'
import { LoginPage } from '@/pages/LoginPage'
import { PlaceholderPage } from '@/pages/PlaceholderPage'
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
          {
            path: 'containers',
            element: (
              <PlaceholderPage
                title="Containers"
                phase="fase 3"
                icon={Server}
                items={['Lista, status e healthcheck', 'CPU/memória por container', 'Logs em tempo real']}
              />
            ),
          },
          {
            path: 'applications',
            element: (
              <PlaceholderPage
                title="Aplicações"
                phase="fase 3/5"
                icon={Boxes}
                items={['Agrupamento por projeto Compose', 'Status consolidado', 'Metadados e URL de acesso']}
              />
            ),
          },
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
