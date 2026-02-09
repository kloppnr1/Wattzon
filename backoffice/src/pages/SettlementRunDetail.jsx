import { useState, useEffect, useCallback } from 'react';
import { useParams, Link } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';

const PAGE_SIZE = 50;

const statusStyles = {
  running: { dot: 'bg-teal-400', badge: 'bg-teal-50 text-teal-700' },
  completed: { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700' },
  failed: { dot: 'bg-rose-400', badge: 'bg-rose-50 text-rose-700' },
};

function StatusBadge({ status, label }) {
  const cfg = statusStyles[status] || statusStyles.running;
  return (
    <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${cfg.badge}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${cfg.dot}`} />
      {label}
    </span>
  );
}

export default function SettlementRunDetail() {
  const { t } = useTranslation();
  const { id } = useParams();
  const [run, setRun] = useState(null);
  const [linesData, setLinesData] = useState(null);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [linesLoading, setLinesLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    setLoading(true);
    setError(null);
    api.getSettlementRun(id)
      .then(setRun)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [id]);

  const fetchLines = useCallback((p) => {
    api.getSettlementLines(id, { page: p, pageSize: PAGE_SIZE })
      .then(setLinesData)
      .catch((e) => setError(e.message))
      .finally(() => setLinesLoading(false));
  }, [id]);

  useEffect(() => { fetchLines(page); }, [page, fetchLines]);

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-[3px] border-teal-100 border-t-teal-500 rounded-full animate-spin" />
          <p className="text-sm text-slate-400 font-medium">{t('runDetail.loadingRun')}</p>
        </div>
      </div>
    );
  }

  if (error || !run) {
    return (
      <div className="p-8 max-w-6xl mx-auto">
        <div className="text-center text-rose-600">Error: {error || 'Run not found'}</div>
      </div>
    );
  }

  const lines = linesData?.items ?? [];
  const totalCount = linesData?.totalCount ?? 0;
  const totalPages = Math.ceil(totalCount / PAGE_SIZE);

  return (
    <div className="p-8 max-w-6xl mx-auto">
      {/* Breadcrumb */}
      <div className="mb-4 flex items-center gap-2 text-sm text-slate-500">
        <Link to="/billing" className="hover:text-teal-600">{t('runDetail.breadcrumbBilling')}</Link>
        <span>/</span>
        <Link to={`/billing/periods/${run.billingPeriodId}`} className="hover:text-teal-600">{t('runDetail.breadcrumbPeriod')}</Link>
        <span>/</span>
        <span className="text-slate-900 font-medium">{t('runDetail.breadcrumbRun', { version: run.version })}</span>
      </div>

      {/* Page header */}
      <div className="mb-6 animate-fade-in-up">
        <h1 className="text-3xl font-bold text-slate-900 tracking-tight">{t('runDetail.title')}</h1>
        <div className="flex items-center gap-3 mt-1">
          <p className="text-base text-slate-500">{t('runDetail.version', { version: run.version })}</p>
          <StatusBadge status={run.status} label={t('status.' + run.status)} />
        </div>
      </div>

      {/* Metrics cards */}
      <div className="grid grid-cols-3 gap-4 mb-6 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <div className="bg-gradient-to-br from-white to-slate-50 rounded-xl p-5 shadow-sm border border-slate-100">
          <div className="text-sm font-medium text-slate-500 mb-1">{t('runDetail.meteringPoints')}</div>
          <div className="text-3xl font-bold text-slate-900">{run.meteringPointsCount}</div>
        </div>
        <div className="bg-gradient-to-br from-white to-teal-50/30 rounded-xl p-5 shadow-sm border border-teal-100/50">
          <div className="text-sm font-medium text-teal-600 mb-1">{t('runDetail.totalAmount')}</div>
          <div className="text-3xl font-bold text-teal-700">{run.totalAmount.toFixed(2)} DKK</div>
        </div>
        <div className="bg-gradient-to-br from-white to-emerald-50/30 rounded-xl p-5 shadow-sm border border-emerald-100/50">
          <div className="text-sm font-medium text-emerald-600 mb-1">{t('runDetail.totalVat')}</div>
          <div className="text-3xl font-bold text-emerald-700">{run.totalVat.toFixed(2)} DKK</div>
        </div>
      </div>

      {/* Run info card */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-6 mb-6 animate-fade-in-up" style={{ animationDelay: '120ms' }}>
        <h2 className="text-lg font-semibold text-slate-900 mb-4">{t('runDetail.runInfo')}</h2>
        <dl className="grid grid-cols-2 gap-x-8 gap-y-4">
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('runDetail.period')}</dt>
            <dd className="text-base font-semibold text-slate-900 mt-1">{run.periodStart} to {run.periodEnd}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('runDetail.status')}</dt>
            <dd className="mt-1">
              <StatusBadge status={run.status} label={t('status.' + run.status)} />
            </dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('runDetail.versionLabel')}</dt>
            <dd className="text-base text-slate-700 mt-1">{run.version}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('runDetail.gridArea')}</dt>
            <dd className="text-base text-slate-500 mt-1">{run.gridAreaCode || '-'}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('runDetail.executedAt')}</dt>
            <dd className="text-base text-slate-700 mt-1">{new Date(run.executedAt).toLocaleString()}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('runDetail.completedAt')}</dt>
            <dd className="text-base text-slate-700 mt-1">
              {run.completedAt ? new Date(run.completedAt).toLocaleString() : '-'}
            </dd>
          </div>
        </dl>
        {run.errorDetails && (
          <div className="mt-4 p-4 bg-rose-50 border border-rose-200 rounded-lg">
            <div className="text-sm font-medium text-rose-700 mb-1">{t('runDetail.errorDetails')}</div>
            <div className="text-sm text-rose-600">{run.errorDetails}</div>
          </div>
        )}
      </div>

      {/* Settlement lines */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden animate-fade-in-up" style={{ animationDelay: '180ms' }}>
        <div className="px-6 py-4 border-b border-slate-200">
          <h2 className="text-lg font-semibold text-slate-900">{t('runDetail.settlementLines')}</h2>
          <p className="text-sm text-slate-500 mt-1">{t('runDetail.totalLines', { count: totalCount })}</p>
        </div>

        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-slate-200">
            <thead className="bg-slate-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('runDetail.colMeteringPoint')}</th>
                <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('runDetail.colChargeType')}</th>
                <th className="px-6 py-3 text-right text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('runDetail.colKwh')}</th>
                <th className="px-6 py-3 text-right text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('runDetail.colAmount')}</th>
                <th className="px-6 py-3 text-right text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('runDetail.colVat')}</th>
                <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('runDetail.colCurrency')}</th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-slate-100">
              {linesLoading && lines.length === 0 ? (
                <tr>
                  <td colSpan="6" className="px-6 py-12 text-center text-slate-500">
                    {t('common.loading')}
                  </td>
                </tr>
              ) : lines.length === 0 ? (
                <tr>
                  <td colSpan="6" className="px-6 py-12 text-center text-slate-500">
                    {t('runDetail.noLinesFound')}
                  </td>
                </tr>
              ) : (
                lines.map((line) => (
                  <tr key={line.id} className="hover:bg-slate-50 transition-colors">
                    <td className="px-6 py-4 whitespace-nowrap text-sm font-medium text-slate-900">{line.meteringPointGsrn}</td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <span className="inline-flex px-2 py-1 text-xs font-medium rounded-full bg-slate-100 text-slate-700">
                        {line.chargeType}
                      </span>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm text-slate-700">{line.totalKwh.toFixed(3)}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium text-slate-900">{line.totalAmount.toFixed(2)}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-right text-sm text-slate-700">{line.vatAmount.toFixed(2)}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-slate-500">{line.currency}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>

        {/* Pagination */}
        {totalPages > 1 && (
          <div className="px-6 py-4 bg-slate-50 border-t border-slate-200 flex items-center justify-between">
            <div className="text-sm text-slate-600">
              {t('common.showingRange', { from: (page - 1) * PAGE_SIZE + 1, to: Math.min(page * PAGE_SIZE, totalCount), total: totalCount })} {t('runDetail.showingLines')}
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
