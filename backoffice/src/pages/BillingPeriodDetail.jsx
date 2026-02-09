import { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';

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
      {label || status}
    </span>
  );
}

export default function BillingPeriodDetail() {
  const { t } = useTranslation();
  const { id } = useParams();
  const [period, setPeriod] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    setLoading(true);
    setError(null);
    api.getBillingPeriod(id)
      .then(setPeriod)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [id]);

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-[3px] border-teal-100 border-t-teal-500 rounded-full animate-spin" />
          <p className="text-sm text-slate-400 font-medium">{t('billingDetail.loadingPeriod')}</p>
        </div>
      </div>
    );
  }

  if (error || !period) {
    return (
      <div className="p-8 max-w-6xl mx-auto">
        <div className="text-center text-rose-600">Error: {error || 'Period not found'}</div>
      </div>
    );
  }

  return (
    <div className="p-8 max-w-6xl mx-auto">
      {/* Breadcrumb */}
      <div className="mb-4 flex items-center gap-2 text-sm text-slate-500">
        <Link to="/billing" className="hover:text-teal-600">{t('billingDetail.breadcrumbBilling')}</Link>
        <span>/</span>
        <span className="text-slate-900 font-medium">{t('billingDetail.breadcrumbPeriod', { start: period.periodStart })}</span>
      </div>

      {/* Page header */}
      <div className="mb-6 animate-fade-in-up">
        <h1 className="text-3xl font-bold text-slate-900 tracking-tight">{t('billingDetail.title')}</h1>
        <p className="text-base text-slate-500 mt-1">{period.periodStart} to {period.periodEnd}</p>
      </div>

      {/* Period info card */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-6 mb-6 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <h2 className="text-lg font-semibold text-slate-900 mb-4">{t('billingDetail.periodInfo')}</h2>
        <dl className="grid grid-cols-2 gap-x-8 gap-y-4">
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('billingDetail.periodStart')}</dt>
            <dd className="text-base font-semibold text-slate-900 mt-1">{period.periodStart}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('billingDetail.periodEnd')}</dt>
            <dd className="text-base font-semibold text-slate-900 mt-1">{period.periodEnd}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('billingDetail.frequency')}</dt>
            <dd className="mt-1">
              <span className="inline-flex px-2 py-1 text-sm font-medium rounded-full bg-slate-100 text-slate-700">
                {period.frequency}
              </span>
            </dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('billingDetail.createdAt')}</dt>
            <dd className="text-base text-slate-700 mt-1">{new Date(period.createdAt).toLocaleString()}</dd>
          </div>
        </dl>
      </div>

      {/* Settlement runs */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden animate-fade-in-up" style={{ animationDelay: '120ms' }}>
        <div className="px-6 py-4 border-b border-slate-200">
          <h2 className="text-lg font-semibold text-slate-900">{t('billingDetail.settlementRuns')}</h2>
          <p className="text-sm text-slate-500 mt-1">{t('billingDetail.totalRuns', { count: period.settlementRuns.length })}</p>
        </div>

        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-slate-200">
            <thead className="bg-slate-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('billingDetail.colRun')}</th>
                <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('billingDetail.colStatus')}</th>
                <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('billingDetail.colMeteringPoints')}</th>
                <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('billingDetail.colGridArea')}</th>
                <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('billingDetail.colExecutedAt')}</th>
                <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('billingDetail.colCompletedAt')}</th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-slate-100">
              {period.settlementRuns.length === 0 ? (
                <tr>
                  <td colSpan="6" className="px-6 py-12 text-center text-slate-500">
                    {t('billingDetail.noRunsFound')}
                  </td>
                </tr>
              ) : (
                period.settlementRuns.map((run) => (
                  <tr key={run.id} className="hover:bg-slate-50 transition-colors cursor-pointer">
                    <td className="px-6 py-4 whitespace-nowrap">
                      <Link to={`/billing/runs/${run.id}`} className="text-teal-600 font-medium hover:text-teal-700">
                        v{run.version}
                      </Link>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <StatusBadge status={run.status} label={t('status.' + run.status)} />
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-slate-700">{run.meteringPointsCount}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-slate-500">{run.gridAreaCode || '-'}</td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-slate-500">
                      {new Date(run.executedAt).toLocaleString()}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-slate-500">
                      {run.completedAt ? new Date(run.completedAt).toLocaleString() : '-'}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
