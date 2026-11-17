import { useState, type FormEvent } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router'
import { ApiError } from '@/api/client'
import { useLogin, useMe } from '@/api/hooks'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'

export function LoginPage() {
  const { data: me } = useMe()
  const login = useLogin()
  const navigate = useNavigate()
  const location = useLocation()
  const [username, setUsername] = useState('admin')
  const [password, setPassword] = useState('')
  const from = (location.state as { from?: string } | null)?.from ?? '/'

  if (me) return <Navigate to={from} replace />

  const onSubmit = (e: FormEvent) => {
    e.preventDefault()
    login.mutate({ username, password }, { onSuccess: () => navigate(from, { replace: true }) })
  }

  const error =
    login.error instanceof ApiError
      ? login.error.status === 429
        ? 'Muitas tentativas. Aguarde um minuto.'
        : login.error.message
      : login.error
        ? 'Falha ao conectar com a API.'
        : null

  return (
    <div className="grid min-h-full place-items-center p-4">
      <Card className="w-full max-w-sm">
        <CardContent className="space-y-6 p-6">
          <div className="space-y-1 text-center">
            <img src="/favicon.svg" alt="" className="mx-auto size-10" />
            <h1 className="text-xl font-semibold">VPS Control</h1>
            <p className="text-sm text-muted-foreground">Acesso restrito à tailnet</p>
          </div>
          <form onSubmit={onSubmit} className="space-y-4">
            <label className="block space-y-1.5 text-sm">
              <span>Usuário</span>
              <Input
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                autoComplete="username"
                required
              />
            </label>
            <label className="block space-y-1.5 text-sm">
              <span>Senha</span>
              <Input
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                autoComplete="current-password"
                autoFocus
                required
              />
            </label>
            {error && (
              <p role="alert" className="text-sm text-danger">
                {error}
              </p>
            )}
            <Button type="submit" className="w-full" disabled={login.isPending}>
              {login.isPending ? 'Entrando…' : 'Entrar'}
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  )
}
