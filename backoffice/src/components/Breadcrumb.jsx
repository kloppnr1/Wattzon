import { Link, useSearchParams } from 'react-router-dom';
import { useTranslation } from '../i18n/LanguageContext';

const fromLabelKeys = {
  '/customers/': 'breadcrumb.customer',
  '/invoices/': 'breadcrumb.invoice',
  '/signups/': 'breadcrumb.signup',
  '/payments/': 'breadcrumb.payment',
  '/datahub/processes/': 'breadcrumb.process',
};

function resolveFromLabel(fromPath, fromLabel, t) {
  if (fromLabel) {
    return { label: fromLabel, to: fromPath };
  }
  for (const [prefix, key] of Object.entries(fromLabelKeys)) {
    if (fromPath.startsWith(prefix)) {
      return { label: t(key), to: fromPath };
    }
  }
  return null;
}

export default function Breadcrumb({ fallback = [], current }) {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();
  const from = searchParams.get('from');
  const fromLabel = searchParams.get('fromLabel');

  const segments = [];

  if (from) {
    const context = resolveFromLabel(from, fromLabel, t);
    if (context) {
      segments.push(context);
    }
  }

  if (segments.length === 0) {
    fallback.forEach((seg) => segments.push(seg));
  }

  return (
    <div className="mb-4 animate-fade-in-up">
      <div className="flex items-center gap-2 text-sm text-slate-500">
        {segments.map((seg, i) => (
          <span key={i} className="flex items-center gap-2">
            {i > 0 && <span>/</span>}
            <Link to={seg.to} className="hover:text-teal-600 transition-colors">
              {seg.label}
            </Link>
          </span>
        ))}
        {segments.length > 0 && <span>/</span>}
        <span className="text-slate-900 font-medium">{current}</span>
      </div>
    </div>
  );
}
