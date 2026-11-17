import { ArrowLeft, ExternalLink, Pencil } from 'lucide-react'
import { useState, type FormEvent } from 'react'
import { Link, useParams } from 'react-router'
import { ApiError, PERMISSIONS, type ApplicationView } from '@/api/client'
import { useApplication, useHasPermission, useUpdateApplication } from '@/api/hooks'
import { ApplicationStatusBadge } from '@/components/ContainerStatus'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { formatBytes, formatPercent } from '@/lib/format'
import { ContainerTable } from '@/pages/containers/ContainersPage'

export function ApplicationDetailPage() {
  const { slug = '' } = useParams()
  const { data, isPending, error } = useApplication(slug)
  const canEdit = useHasPermission(PERMISSIONS.applicationsEdit)
  const [editing, setEditing] = useState(false)

  if (isPending) return <Skeleton className="h-40 w-full" />
  if (error || !data) {
    return (
      <div className="space-y-3 py-10 text-center">
        <p className="font-medium">
          {error instanceof ApiError && error.status === 404
            ? 'Aplicação não encontrada.'
            : 'Falha ao carregar.'}
        </p>
        <Link to="/applications" className="text-sm text-primary hover:underline">
          Voltar para aplicações
        </Link>
      </div>
    )
  }

  const a = data.application

  return (
    <div className="space-y-6">
      <div className="space-y-3">
        <Link
          to="/applications"
          className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
        >
          <ArrowLeft className="size-4" /> Aplicações
        </Link>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="space-y-1">
            <h1 className="flex flex-wrap items-center gap-2 text-2xl font-semibold tracking-tight">
              {a.displayName} <ApplicationStatusBadge status={a.status} />
            </h1>
            <p className="font-mono text-sm text-muted-foreground">projeto {a.slug}</p>
            {a.description && <p className="max-w-2xl text-sm">{a.description}</p>}
            {a.url && (
              <a
                href={a.url}
                target="_blank"
                rel="noreferrer noopener"
                className="inline-flex items-center gap-1 text-sm text-primary hover:underline"
              >
                <ExternalLink className="size-3.5" /> {a.url}
              </a>
            )}
          </div>
          {canEdit && !editing && (
            <Button variant="outline" onClick={() => setEditing(true)}>
              <Pencil /> Editar
            </Button>
          )}
        </div>
      </div>

      {editing && <EditForm app={a} onDone={() => setEditing(false)} />}

      <div className="grid gap-4 sm:grid-cols-3">
        <Stat label="Containers" value={`${a.runningCount}/${a.containerCount} rodando`} />
        <Stat label="CPU agregada" value={formatPercent(a.cpuPercent)} />
        <Stat label="Memória agregada" value={formatBytes(a.memoryUsageBytes)} />
      </div>

      <section className="space-y-2">
        <h2 className="text-sm font-medium text-muted-foreground">Componentes</h2>
        {data.containers.length === 0 ? (
          <Card>
            <CardContent className="p-6 text-sm text-muted-foreground">
              Nenhum container deste projeto existe no momento.
            </CardContent>
          </Card>
        ) : (
          <Card className="overflow-hidden">
            <ContainerTable items={data.containers} />
          </Card>
        )}
      </section>
    </div>
  )
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>{label}</CardTitle>
      </CardHeader>
      <CardContent className="text-xl font-semibold tabular-nums">{value}</CardContent>
    </Card>
  )
}

function EditForm({ app, onDone }: { app: ApplicationView; onDone: () => void }) {
  const update = useUpdateApplication(app.slug)
  const [displayName, setDisplayName] = useState(app.hasMetadata ? app.displayName : '')
  const [description, setDescription] = useState(app.description ?? '')
  const [url, setUrl] = useState(app.url ?? '')

  const submit = (e: FormEvent) => {
    e.preventDefault()
    update.mutate(
      { displayName: displayName || null, description: description || null, url: url || null },
      { onSuccess: onDone },
    )
  }

  return (
    <Card>
      <CardContent className="p-4">
        <form onSubmit={submit} className="grid gap-4 md:grid-cols-2">
          <label className="space-y-1.5 text-sm">
            <span>Nome de exibição</span>
            <Input
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              placeholder={app.slug}
              maxLength={80}
            />
          </label>
          <label className="space-y-1.5 text-sm">
            <span>URL de acesso</span>
            <Input
              type="url"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              placeholder="https://app.exemplo.com"
              maxLength={300}
            />
          </label>
          <label className="space-y-1.5 text-sm md:col-span-2">
            <span>Descrição</span>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              maxLength={500}
              rows={3}
              className="w-full rounded-md border border-border bg-card px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-primary/50"
            />
          </label>
          {update.error && (
            <p className="text-sm text-danger md:col-span-2">
              {update.error instanceof ApiError ? update.error.message : 'Falha ao salvar.'}
            </p>
          )}
          <div className="flex justify-end gap-2 md:col-span-2">
            <Button type="button" variant="outline" onClick={onDone} disabled={update.isPending}>
              Cancelar
            </Button>
            <Button type="submit" disabled={update.isPending}>
              {update.isPending ? 'Salvando…' : 'Salvar'}
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  )
}
