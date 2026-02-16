import { useState, useEffect, useCallback } from 'react';
import { useParams, Link } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';
import Breadcrumb from '../components/Breadcrumb';
import WattzonLoader from '../components/WattzonLoader';

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

const triggerStyles = {
  manual: 'bg-blue-50 text-blue-700',
  auto: 'bg-purple-50 text-purple-700',
};

export default function SettlementRunDetail() {
  const { t } = useTranslation();
  const { id } = useParams();
  const [run, setRun] = useState(null);
  const [linesData, setLinesData] = useState(null);
  const [corrections, setCorrections] = useState([]);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [linesLoading, setLinesLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    setLoading(true);
    setError(null);
    Promise.all([
      api.getSettlementRun(id),
      api.getRunCorrections(id).catch(() => []),
    ])
      .then(([runData, correctionsData]) => {
        setRun(runData);
        setCorrections(correctionsData);
      })
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
      <WattzonLoader message={t('runDetail.loadingRun')} />
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

  const chargeTypeLabel = (type) => t('chargeType.' + type) !== 'chargeType.' + type
    ? t('chargeType.' + type)
    : type.replace(/_/g, ' ').replace(/\b\w/g, c => c.toUpperCase());

  return (
    <div className="p-4 sm:p-8 max-w-6xl mx-auto">
      <Breadcrumb
        fallback={[{ label: t('settlement.title'), to: '/settlement' }]}
        current={t('runDetail.breadcrumbRun', { version: run.version })}
      />

      {/* Page header */}
      <div className="mb-6 animate-fade-in-up">
        <h1 className="text-2xl sm:text-3xl font-bold text-slate-900 tracking-tight">{t('runDetail.title')}</h1>
        <div className="flex items-center gap-3 mt-1">
          <p className="text-base text-slate-500">{t('runDetail.version', { version: run.version })}</p>
          <StatusBadge status={run.status} label={t('status.' + run.status)} />
        </div>
      </div>

      {/* Metrics cards */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-6 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <div className="bg-gradient-to-br from-white to-slate-50 rounded-xl p-5 shadow-sm border border-slate-100">
          <div className="text-sm font-medium text-slate-500 mb-1">{t('runDetail.meteringPoint')}</div>
          <div className="text-lg font-bold font-mono text-slate-900">
            {run.customerId ? (
              <Link to={`/customers/${run.customerId}`} className="text-teal-600 hover:text-teal-700">{run.meteringPointId}</Link>
            ) : (
              run.meteringPointId || '-'
            )}
          </div>
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
        <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-4 sm:gap-x-8 gap-y-4">
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
            <dt className="text-sm font-medium text-slate-500">{t('runDetail.meteringPoint')}</dt>
            <dd className="text-base font-mono text-slate-700 mt-1">{run.meteringPointId || '-'}</dd>
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

      {/* Settlement lines grouped by metering point */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden animate-fade-in-up" style={{ animationDelay: '180ms' }}>
        <div className="px-5 py-3.5 border-b border-slate-200">
          <h2 className="text-lg font-semibold text-slate-900">{t('runDetail.settlementLines')}</h2>
          <p className="text-sm text-slate-500 mt-1">{t('runDetail.totalLines', { count: totalCount })}</p>
        </div>

        {linesLoading && lines.length === 0 ? (
          <div className="px-4 py-12 text-center text-slate-500">{t('common.loading')}</div>
        ) : lines.length === 0 ? (
          <div className="px-4 py-12 text-center text-slate-500">{t('runDetail.noLinesFound')}</div>
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-[600px] w-full">
              <thead>
                <tr className="bg-slate-50 border-b border-slate-200">
                  <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('runDetail.colChargeType')}</th>
                  <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('runDetail.colKwh')}</th>
                  <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('runDetail.colAmount')}</th>
                  <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('runDetail.colVat')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {lines.map((line) => (
                  <tr key={line.id} className="hover:bg-slate-50 transition-colors">
                    <td className="px-4 py-2.5 text-sm text-slate-700">{chargeTypeLabel(line.chargeType)}</td>
                    <td className="px-4 py-2.5 text-right text-sm text-slate-600 tabular-nums">{line.totalKwh.toFixed(3)}</td>
                    <td className="px-4 py-2.5 text-right text-sm text-slate-900 tabular-nums">{line.totalAmount.toFixed(2)}</td>
                    <td className="px-4 py-2.5 text-right text-sm text-slate-600 tabular-nums">{line.vatAmount.toFixed(2)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Pagination */}
        {totalPages > 1 && (
          <div className="px-5 py-3.5 bg-slate-50 border-t border-slate-200 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2">
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

      {/* Related Corrections */}
      {corrections.length > 0 && (
        <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden mt-6 animate-fade-in-up" style={{ animationDelay: '240ms' }}>
          <div className="px-5 py-3.5 border-b border-slate-200">
            <h2 className="text-lg font-semibold text-slate-900">{t('runDetail.relatedCorrections')}</h2>
            <p className="text-sm text-slate-500 mt-1">{t('runDetail.relatedCorrectionsSubtitle')}</p>
          </div>
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr className="bg-slate-50 border-b border-slate-200">
                  <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('runDetail.corrColId')}</th>
                  <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('runDetail.corrColMeteringPoint')}</th>
                  <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('runDetail.corrColTrigger')}</th>
                  <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('runDetail.corrColDeltaKwh')}</th>
                  <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('runDetail.corrColTotal')}</th>
                  <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('runDetail.corrColCreated')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {corrections.map((c) => (
                  <tr key={c.correctionBatchId} className="hover:bg-slate-50 transition-colors">
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <Link to={`/billing/corrections/${c.correctionBatchId}`} className="text-teal-600 font-medium hover:text-teal-700 font-mono text-sm">
                        {c.correctionBatchId.substring(0, 8)}...
                      </Link>
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-sm font-mono text-slate-700">{c.meteringPointId}</td>
                    <td className="px-4 py-2.5 whitespace-nowrap">
                      <span className={`inline-flex px-2 py-0.5 rounded-full text-[11px] font-medium ${triggerStyles[c.triggerType] || triggerStyles.manual}`}>
                        {t('corrections.trigger_' + c.triggerType)}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums text-slate-600">{c.totalDeltaKwh.toFixed(3)}</td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-right text-sm tabular-nums font-semibold text-slate-900">{c.total.toFixed(2)} DKK</td>
                    <td className="px-4 py-2.5 whitespace-nowrap text-sm text-slate-500">{new Date(c.createdAt).toLocaleString()}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </div>
  );
}
