/** TurnstileMetaItem — ícone, rótulo e valor: a linha técnica do cartão da catraca. */
export interface TurnstileMetaItemProps {
  icon: React.ReactNode;
  label: string;
  value: React.ReactNode;
}

export function TurnstileMetaItem({ icon, label, value }: TurnstileMetaItemProps) {
  return (
    <div className="inline-flex items-center gap-1.5 text-rayzer-caption">
      <span className="text-rayzer-text-tertiary">{icon}</span>
      <span className="text-rayzer-text-secondary">{label}</span>
      <span className="font-semibold text-rayzer-text">{value}</span>
    </div>
  );
}
