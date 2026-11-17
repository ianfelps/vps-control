import { Boxes, LayoutDashboard, LogOut, Menu, ScrollText, Server, X, type LucideIcon } from 'lucide-react'
import { useState } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router'
import { useLogout, useMe } from '@/api/hooks'
import { useMetricsStream, type StreamState } from '@/api/realtime'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

const NAV: { to: string; label: string; icon: LucideIcon }[] = [
  { to: '/', label: 'Dashboard', icon: LayoutDashboard },
  { to: '/containers', label: 'Containers', icon: Server },
  { to: '/applications', label: 'Aplicações', icon: Boxes },
  { to: '/audit', label: 'Auditoria', icon: ScrollText },
]

const STREAM_LABEL: Record<
  StreamState,
  { text: string; tone: 'success' | 'warning' | 'danger' | 'neutral' }
> = {
  live: { text: 'Ao vivo', tone: 'success' },
  connecting: { text: 'Conectando', tone: 'neutral' },
  reconnecting: { text: 'Reconectando', tone: 'warning' },
  offline: { text: 'Offline', tone: 'danger' },
}

export function AppLayout() {
  const { data: me } = useMe()
  const logout = useLogout()
  const navigate = useNavigate()
  const stream = useMetricsStream(Boolean(me))
  const [open, setOpen] = useState(false)
  const status = STREAM_LABEL[stream]

  const handleLogout = () => logout.mutate(undefined, { onSettled: () => navigate('/login') })

  return (
    <div className="flex min-h-full">
      {open && (
        <div
          className="fixed inset-0 z-30 bg-black/40 md:hidden"
          onClick={() => setOpen(false)}
          aria-hidden
        />
      )}
      <aside
        className={cn(
          'fixed inset-y-0 left-0 z-40 flex w-60 flex-col border-r border-border bg-card transition-transform md:static md:translate-x-0',
          open ? 'translate-x-0' : '-translate-x-full',
        )}
      >
        <div className="flex h-14 items-center justify-between gap-2 border-b border-border px-4">
          <span className="flex items-center gap-2 font-semibold">
            <img src="/favicon.svg" alt="" className="size-6" />
            VPS Control
          </span>
          <Button
            variant="ghost"
            size="icon"
            className="md:hidden"
            onClick={() => setOpen(false)}
            aria-label="Fechar menu"
          >
            <X />
          </Button>
        </div>
        <nav className="flex-1 space-y-1 p-3">
          {NAV.map(({ to, label, icon: Icon }) => (
            <NavLink
              key={to}
              to={to}
              end={to === '/'}
              onClick={() => setOpen(false)}
              className={({ isActive }) =>
                cn(
                  'flex items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors',
                  isActive
                    ? 'bg-primary/10 font-medium text-primary'
                    : 'text-muted-foreground hover:bg-muted',
                )
              }
            >
              <Icon className="size-4" />
              {label}
            </NavLink>
          ))}
        </nav>
        <div className="border-t border-border p-3 text-xs text-muted-foreground">
          <div className="truncate">
            {me?.username}
            {me?.tailscaleUser && <span className="block truncate">{me.tailscaleUser}</span>}
          </div>
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="sticky top-0 z-20 flex h-14 items-center gap-3 border-b border-border bg-background/80 px-4 backdrop-blur">
          <Button
            variant="ghost"
            size="icon"
            className="md:hidden"
            onClick={() => setOpen(true)}
            aria-label="Abrir menu"
          >
            <Menu />
          </Button>
          <div className="flex-1" />
          <Badge tone={status.tone}>
            <span className="size-1.5 rounded-full bg-current" />
            {status.text}
          </Badge>
          <Button variant="ghost" size="sm" onClick={handleLogout} disabled={logout.isPending}>
            <LogOut />
            <span className="hidden sm:inline">Sair</span>
          </Button>
        </header>
        <main className="mx-auto w-full max-w-7xl flex-1 p-4 md:p-6">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
