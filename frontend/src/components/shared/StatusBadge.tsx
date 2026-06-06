import type { HoldStatus } from '../../api/types';

interface Props {
  status: HoldStatus;
}

export function StatusBadge({ status }: Props) {
  return (
    <span className={`status-badge ${status}`}>
      <span className="status-badge-dot" />
      {status}
    </span>
  );
}
