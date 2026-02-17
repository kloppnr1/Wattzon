import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';
import WattzonLoader from '../components/WattzonLoader';

const PAGE_SIZE = 50;

const STATUSES = [
  'pending',
  'sent_to_datahub',
  'acknowledged',
  'effectuation_pending',
  'completed',
  'rejected',
  'cancellation_pending',
  'cancelled',
];

const PROCESS_TYPES = [
  'supplier_switch',
  'move_in',
  'move_out',
  'end_of_supply',
];

const processTypeBadge = (type) => {
  switch (type) {
    case 'supplier_switch':
      return 'bg-blue-50 text-blue-700';
    case 'move_in':
    case 'move_out':
      return 'bg-purple-50 text-purple-700';
    case 'end_of_supply':
      return 'bg-amber-50 text-amber-700';
    default:
      return 'bg-slate-100 text-slate-500';
  }
};

const statusDotColor = (status) => {
  switch (status) {
    case 'pending': return 'bg-slate-400';
    case 'sent_to_datahub': return 'bg-teal-400';
    case 'acknowledged': return 'bg-blue-400';
    case 'effectuation_pending': return 'bg-amber-400';
    case 'completed': return 'bg-emerald-400';
    case 'rejected': return 'bg-rose-400';
    case 'cancellation_pending': return 'bg-amber-500';
    case 'cancelled': return 'bg-slate-400';
    default: return 'bg-slate-400';
  }
};

const statusBadgeColor = (status) => {
  switch (status) {
    case 'pending': return 'bg-slate-100 text-slate-600';
    case 'sent_to_datahub': return 'bg-teal-50 text-teal-700';
    case 'acknowledged': return 'bg-blue-50 text-blue-700';
    case 'effectuation_pending': return 'bg-amber-50 text-amber-700';
    case 'completed': return 'bg-emerald-50 text-emerald-700';
    case 'rejected': return 'bg-rose-50 text-rose-700';
    case 'cancellation_pending': return 'bg-amber-50 text-amber-700';
    case 'cancelled': return 'bg-slate-100 text-slate-500';
    default: return 'bg-slate-100 text-slate-500';
  }
};

export default function Processes() {
  const { t } = useTranslation();
  const [data, setData] = useState(null);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [status, setStatus] = useState('');
  const [processType, setProcessType] = useState('');
  const [search, setSearch] = useState('');

  const fetchPage = useCallback((p) => {
    setError(null);
    api.getProcesses({
      status: status || undefined,
      processType: processType || undefined,
      search: search || undefined,
      page: p,
      pageSize: PAGE_SIZE,
    })
      .then(setData)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [status, processType, search]);

  useEffect(() => { setPage(1); }, [status, processType, search]);
  useEffect(() => { fetchPage(page); }, [page, fetchPage]);

  const processes = data?.items ?? [];
  const totalCount = data?.totalCount ?? 0;
  const totalPages = Math.ceil(totalCount / PAGE_SIZE);

  if (loading) {
    return <WattzonLoader message={t('processes.loading')} />;
  }

  return (
    <div className="p-4 sm:p-8 max-w-6xl mx-auto">
      {/* Page header */}
      <div className="mb-6 animate-fade-in-up">
        <h1 className="text-2xl sm:text-3xl font-bold text-slate-900 tracking-tight">{t('processes.title')}</h1>
        <p className="text-base text-slate-500 mt-1">{t('processes.subtitle')}</p>
      </div>

      {/* Filter bar */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-4 mb-6 animate-fade-in-up" style={{ animationDelay: '40ms' }}>
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
          <input
            type="text"
            placeholder={t('processes.searchPlaceholder')}
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
          />
          <select
            value={status}
            onChange={(e) => setStatus(e.target.value)}
            className="px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
          >
            <option value="">{t('processes.allStatuses')}</option>
            {STATUSES.map((s) => (
              <option key={s} value={s}>{t('status.' + s)}</option>
            ))}
          </select>
          <select
            value={processType}
            onChange={(e) => setProcessType(e.target.value)}
            className="px-3 py-2 rounded-lg border border-slate-200 text-sm focus:outline-none focus:ring-2 focus:ring-teal-500/20 focus:border-teal-400"
          >
            <option value="">{t('processes.allTypes')}</option>
            {PROCESS_TYPES.map((pt) => (
              <option key={pt} value={pt}>{t('processType.' + pt)}</option>
            ))}
          </select>
        </div>
      </div>

      {/* Stat card */}
      <div className="grid grid-cols-1 gap-4 mb-6 animate-fade-in-up" style={{ animationDelay: '80ms' }}>
        <div className="bg-gradient-to-br from-white to-teal-50/30 rounded-xl p-5 shadow-sm border border-teal-100/50">
          <div className="text-sm font-medium text-teal-600 mb-1">{t('processes.totalProcesses')}</div>
          <div className="text-3xl font-bold text-teal-700">{totalCount}</div>
        </div>
      </div>

      {/* Process table */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden animate-fade-in-up" style={{ animationDelay: '120ms' }}>
        {error && (
          <div className="px-5 py-3 bg-rose-50 border-b border-rose-200">
            <p className="text-sm text-rose-600">{error}</p>
          </div>
        )}

        <div className="overflow-x-auto">
          <table className="w-full min-w-[750px]">
            <thead>
              <tr className="bg-slate-50 border-b border-slate-200">
                <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('processes.colType')}</th>
                <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('processes.colGsrn')}</th>
                <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('processes.colCustomer')}</th>
                <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('processes.colStatus')}</th>
                <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('processes.colEffectiveDate')}</th>
                <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('processes.colCreated')}</th>
                <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5"></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {processes.length === 0 ? (
                <tr>
                  <td colSpan="7" className="px-6 py-12 text-center">
                    <p className="text-sm text-slate-500">{t('processes.noProcesses')}</p>
                  </td>
                </tr>
              ) : (
                processes.map((p, i) => (
                  <tr
                    key={p.id}
                    className="hover:bg-slate-50 transition-colors animate-slide-in"
                    style={{ animationDelay: `${i * 40}ms` }}
                  >
                    <td className="px-4 py-2.5">
                      <span className={`inline-flex px-2 py-0.5 rounded-full text-[11px] font-medium ${processTypeBadge(p.processType)}`}>
                        {t('processType.' + p.processType) || p.processType}
                      </span>
                    </td>
                    <td className="px-4 py-2.5">
                      <span className="text-[11px] font-mono text-slate-500 bg-slate-100 px-1.5 py-0.5 rounded">{p.gsrn}</span>
                    </td>
                    <td className="px-4 py-2.5 text-sm text-slate-700">
                      {p.customerId ? (
                        <Link to={`/customers/${p.customerId}`} className="text-teal-600 hover:text-teal-700">{p.customerName}</Link>
                      ) : (
                        <span className="text-slate-300">&mdash;</span>
                      )}
                    </td>
                    <td className="px-4 py-2.5">
                      <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${statusBadgeColor(p.status)}`}>
                        <span className={`w-1.5 h-1.5 rounded-full ${statusDotColor(p.status)}`} />
                        {t('status.' + p.status)}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 text-sm text-slate-700">{p.effectiveDate || <span className="text-slate-300">&mdash;</span>}</td>
                    <td className="px-4 py-2.5 text-sm text-slate-500">
                      {new Date(p.createdAt).toLocaleDateString('da-DK')}
                    </td>
                    <td className="px-4 py-2.5">
                      <Link
                        to={`/datahub/processes/${p.id}`}
                        className="text-xs font-medium text-teal-600 hover:text-teal-700"
                      >
                        View
                      </Link>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {totalPages > 1 && (
          <div className="px-5 py-3.5 bg-slate-50 border-t border-slate-200 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2">
            <div className="text-sm text-slate-600">
              {t('common.showingRange', { from: (page - 1) * PAGE_SIZE + 1, to: Math.min(page * PAGE_SIZE, totalCount), total: totalCount })} {t('processes.showingProcesses')}
            </div>
            <div className="flex gap-2">
              <button
                onClick={() => setPage(p => Math.max(1, p - 1))}
                disabled={page === 1}
                className="px-3 py-1.5 text-sm font-medium rounded-lg bg-white border border-slate-300 text-slate-700 hover:bg-slate-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                {t('common.previous')}
              </button>
              <button
                onClick={() => setPage(p => Math.min(totalPages, p + 1))}
                disabled={page === totalPages}
                className="px-3 py-1.5 text-sm font-medium rounded-lg bg-white border border-slate-300 text-slate-700 hover:bg-slate-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
              >
                {t('common.next')}
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
