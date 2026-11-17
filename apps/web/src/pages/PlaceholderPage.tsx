import type { LucideIcon } from 'lucide-react'
import { Card, CardContent } from '@/components/ui/card'

export function PlaceholderPage({
  title,
  phase,
  icon: Icon,
  items,
}: {
  title: string
  phase: string
  icon: LucideIcon
  items: string[]
}) {
  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      <Card>
        <CardContent className="flex flex-col items-center gap-3 p-10 text-center">
          <Icon className="size-10 text-muted-foreground" />
          <p className="font-medium">Planejado para a {phase}</p>
          <ul className="space-y-1 text-sm text-muted-foreground">
            {items.map((i) => (
              <li key={i}>{i}</li>
            ))}
          </ul>
        </CardContent>
      </Card>
    </div>
  )
}
