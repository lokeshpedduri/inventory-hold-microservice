import { ApiError } from '../../api/types';

interface Props {
  error: unknown;
}

function extractMessage(error: unknown): string {
  if (error instanceof ApiError) {
    return error.detail ?? error.problem.title;
  }
  if (error instanceof Error) return error.message;
  return 'An unexpected error occurred.';
}

export function ErrorBanner({ error }: Props) {
  return (
    <div className="error-banner" role="alert">
      <span className="error-banner-icon">⚠</span>
      <span>{extractMessage(error)}</span>
    </div>
  );
}
