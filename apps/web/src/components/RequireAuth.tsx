import { Navigate, Outlet, useLocation } from 'react-router'
import { useMe } from '@/api/hooks'

export function RequireAuth() {
  const { data: me, isPending } = useMe()
  const location = useLocation()

  if (isPending) {
    return <div className="grid h-full place-items-center text-sm text-muted-foreground">Carregando…</div>
  }

  if (!me) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  }

  return <Outlet />
}
