interface Props {
  label?: string;
}

export function Spinner({ label = 'Loading…' }: Props) {
  return <span className="spinner" role="status" aria-label={label} />;
}
