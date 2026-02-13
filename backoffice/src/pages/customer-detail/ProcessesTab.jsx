import { useState, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../../api';
import { useTranslation } from '../../i18n/LanguageContext';

const processStatusStyles = {
  pending: { dot: 'bg-amber-400', badge: 'bg-amber-50 text-amber-700' },
  sent_to_datahub: { dot: 'bg-orange-400', badge: 'bg-orange-50 text-orange-700' },
  acknowledged: { dot: 'bg-sky-400', badge: 'bg-sky-50 text-sky-700' },
  effectuation_pending: { dot: 'bg-blue-400', badge: 'bg-blue-50 text-blue-700' },
  completed: { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700' },
  rejected: { dot: 'bg-rose-400', badge: 'bg-rose-50 text-rose-700' },
  cancellation_pending: { dot: 'bg-amber-500', badge: 'bg-amber-50 text-amber-700' },
  cancelled: { dot: 'bg-slate-400', badge: 'bg-slate-100 text-slate-600' },
  sent: { dot: 'bg-teal-400', badge: 'bg-teal-50 text-teal-700' },
  acknowledged_ok: { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700' },
  acknowledged_error: { dot: 'bg-rose-400', badge: 'bg-rose-50 text-rose-700' },
};

export default function ProcessesTab({ customerId }) {
  const { t } = useTranslation();
  const [processes, setProcesses] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    api.getCustomerProcesses(customerId)
      .then(setProcesses)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [customerId]);

  return (
    <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden animate-fade-in-up">
      {error && <div className="p-4 bg-rose-50 border-b border-rose-100 text-rose-700 text-sm">{error}</div>}
      <div className="overflow-x-auto">
        <table className="w-full">
          <thead>
            <tr className="bg-slate-50 border-b border-slate-200">
              <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('customerDetail.processColGsrn')}</th>
              <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('customerDetail.processColType')}</th>
              <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('customerDetail.processColStatus')}</th>
              <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('customerDetail.processColEffective')}</th>
              <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('customerDetail.processColCorrelation')}</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {loading && !processes ? (
              <tr><td colSpan="5" className="px-4 py-12 text-center text-slate-500">{t('common.loading')}</td></tr>
            ) : !processes || processes.length === 0 ? (
              <tr><td colSpan="5" className="px-4 py-12 text-center text-slate-500">{t('customerDetail.noProcesses')}</td></tr>
            ) : (
              processes.map((p) => {
                const cfg = processStatusStyles[p.status] || { dot: 'bg-slate-400', badge: 'bg-slate-100 text-slate-600' };
                return (
                  <tr key={p.id} className="hover:bg-slate-50 transition-colors">
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <span className="text-[11px] font-mono text-slate-500 bg-slate-100 px-1.5 py-0.5 rounded">{p.gsrn}</span>
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <Link to={`/datahub/processes/${p.id}?from=/customers/${customerId}`} className="text-sm text-teal-600 font-medium hover:text-teal-700">
                        {t(`processType.${p.processType}`)}
                      </Link>
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${cfg.badge}`}>
                        <span className={`w-1.5 h-1.5 rounded-full ${cfg.dot}`} />
                        {t('status.' + p.status)}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-sm text-slate-600">{p.effectiveDate || <span className="text-slate-300">&mdash;</span>}</td>
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      {p.datahubCorrelationId ? (
                        <span className="font-mono text-xs text-slate-400">{p.datahubCorrelationId.slice(0, 8)}</span>
                      ) : (
                        <span className="text-slate-300">&mdash;</span>
                      )}
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
