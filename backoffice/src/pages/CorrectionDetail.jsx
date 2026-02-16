import { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';
import Breadcrumb from '../components/Breadcrumb';
import WattzonLoader from '../components/WattzonLoader';

const statusStyles = {
  completed: { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700' },
  failed: { dot: 'bg-rose-400', badge: 'bg-rose-50 text-rose-700' },
};

const triggerStyles = {
  manual: 'bg-blue-50 text-blue-700',
  auto: 'bg-purple-50 text-purple-700',
};

function StatusBadge({ status, label }) {
  const cfg = statusStyles[status] || statusStyles.completed;
  return (
    <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${cfg.badge}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${cfg.dot}`} />
      {label}
    </span>
  );
}

export default function CorrectionDetail() {
  const { t } = useTranslation();
  const { batchId } = useParams();
  const [detail, setDetail] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    setLoading(true);
    setError(null);
    api.getCorrection(batchId)
      .then(setDetail)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [batchId]);

  if (loading) {
    return (
      <WattzonLoader message={t('correctionDetail.loading')} />
    );
  }

  if (error || !detail) {
    return (
      <div className="p-4 sm:p-8 max-w-6xl mx-auto">
        <div className="text-center text-rose-600">{t('common.error')}: {error || t('common.notFound')}</div>
      </div>
    );
  }

  const chargeTypeLabel = (type) => t('chargeType.' + type) !== 'chargeType.' + type
    ? t('chargeType.' + type)
    : type.replace(/_/g, ' ').replace(/\b\w/g, c => c.toUpperCase());

  return (
    <div className="p-4 sm:p-8 max-w-6xl mx-auto">
      <Breadcrumb
        fallback={[{ label: t('settlement.title'), to: '/settlement' }]}
        current={t('correctionDetail.breadcrumbDetail')}
      />

      {/* Page header */}
      <div className="mb-6 animate-fade-in-up">
        <h1 className="text-2xl sm:text-3xl font-bold text-slate-900 tracking-tight">{t('correctionDetail.title')}</h1>
        <div className="flex items-center gap-3 mt-1">
          <p className="text-sm text-slate-500 font-mono">{detail.correctionBatchId}</p>
          <StatusBadge status={detail.status} label={t('status.' + detail.status)} />
        </div>
      </div>

      {/* Metrics cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 mb-6 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <div className="bg-gradient-to-br from-white to-slate-50 rounded-xl p-5 shadow-sm border border-slate-100">
          <div className="text-sm font-medium text-slate-500 mb-1">{t('correctionDetail.deltaKwh')}</div>
          <div className="text-3xl font-bold text-slate-900">{detail.totalDeltaKwh.toFixed(3)}</div>
        </div>
        <div className="bg-gradient-to-br from-white to-teal-50/30 rounded-xl p-5 shadow-sm border border-teal-100/50">
          <div className="text-sm font-medium text-teal-600 mb-1">{t('correctionDetail.subtotal')}</div>
          <div className="text-3xl font-bold text-teal-700">{detail.subtotal.toFixed(2)} DKK</div>
        </div>
        <div className="bg-gradient-to-br from-white to-emerald-50/30 rounded-xl p-5 shadow-sm border border-emerald-100/50">
          <div className="text-sm font-medium text-emerald-600 mb-1">{t('correctionDetail.vat')}</div>
          <div className="text-3xl font-bold text-emerald-700">{detail.vatAmount.toFixed(2)} DKK</div>
        </div>
        <div className="bg-gradient-to-br from-white to-indigo-50/30 rounded-xl p-5 shadow-sm border border-indigo-100/50">
          <div className="text-sm font-medium text-indigo-600 mb-1">{t('correctionDetail.total')}</div>
          <div className="text-3xl font-bold text-indigo-700">{detail.total.toFixed(2)} DKK</div>
        </div>
      </div>

      {/* Info card */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-6 mb-6 animate-fade-in-up" style={{ animationDelay: '120ms' }}>
        <h2 className="text-lg font-semibold text-slate-900 mb-4">{t('correctionDetail.info')}</h2>
        <dl className="grid grid-cols-1 sm:grid-cols-2 gap-x-4 sm:gap-x-8 gap-y-4">
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('correctionDetail.meteringPoint')}</dt>
            <dd className="text-base font-mono text-slate-900 mt-1">{detail.meteringPointId}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('correctionDetail.period')}</dt>
            <dd className="text-base font-semibold text-slate-900 mt-1">{detail.periodStart} — {detail.periodEnd}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('correctionDetail.triggerType')}</dt>
            <dd className="mt-1">
              <span className={`inline-flex px-2 py-0.5 rounded-full text-[11px] font-medium ${triggerStyles[detail.triggerType] || triggerStyles.manual}`}>
                {t('corrections.trigger_' + detail.triggerType)}
              </span>
            </dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('correctionDetail.originalRun')}</dt>
            <dd className="text-base text-slate-700 mt-1">
              {detail.originalRunId ? (
                <Link to={`/billing/runs/${detail.originalRunId}`} className="text-teal-600 hover:text-teal-700 font-mono text-sm">
                  {detail.originalRunId.substring(0, 8)}...
                </Link>
              ) : '-'}
            </dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('correctionDetail.status')}</dt>
            <dd className="mt-1">
              <StatusBadge status={detail.status} label={t('status.' + detail.status)} />
            </dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-slate-500">{t('correctionDetail.createdAt')}</dt>
            <dd className="text-base text-slate-700 mt-1">{new Date(detail.createdAt).toLocaleString()}</dd>
          </div>
          {detail.note && (
            <div className="sm:col-span-2">
              <dt className="text-sm font-medium text-slate-500">{t('correctionDetail.note')}</dt>
              <dd className="text-base text-slate-700 mt-1">{detail.note}</dd>
            </div>
          )}
        </dl>
      </div>

      {/* Lines table */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden animate-fade-in-up" style={{ animationDelay: '180ms' }}>
        <div className="px-5 py-3.5 border-b border-slate-200">
          <h2 className="text-lg font-semibold text-slate-900">{t('correctionDetail.lines')}</h2>
          <p className="text-sm text-slate-500 mt-1">{t('correctionDetail.linesSubtitle')}</p>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full">
            <thead>
              <tr className="bg-slate-50 border-b border-slate-200">
                <th className="px-4 py-2.5 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('correctionDetail.colChargeType')}</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('correctionDetail.colDeltaKwh')}</th>
                <th className="px-4 py-2.5 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('correctionDetail.colDeltaAmount')}</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {(detail.lines || []).map((line) => (
                <tr key={line.id} className="hover:bg-slate-50 transition-colors">
                  <td className="px-4 py-2.5 text-sm text-slate-700">{chargeTypeLabel(line.chargeType)}</td>
                  <td className="px-4 py-2.5 text-right text-sm text-slate-600 tabular-nums">{line.deltaKwh.toFixed(3)}</td>
                  <td className="px-4 py-2.5 text-right text-sm text-slate-900 tabular-nums font-medium">{line.deltaAmount.toFixed(2)} DKK</td>
                </tr>
              ))}
              <tr className="bg-slate-50/80">
                <td className="px-4 py-2.5 text-sm font-semibold text-slate-900">{t('correctionDetail.subtotal')}</td>
                <td />
                <td className="px-4 py-2.5 text-right text-sm font-semibold text-slate-900 tabular-nums">{detail.subtotal.toFixed(2)} DKK</td>
              </tr>
              <tr className="bg-slate-50/50">
                <td className="px-4 py-2.5 text-sm font-medium text-slate-600">{t('correctionDetail.vat')}</td>
                <td />
                <td className="px-4 py-2.5 text-right text-sm font-medium text-slate-600 tabular-nums">{detail.vatAmount.toFixed(2)} DKK</td>
              </tr>
              <tr className="bg-teal-50/50">
                <td className="px-4 py-2.5 text-sm font-bold text-slate-900">{t('correctionDetail.total')}</td>
                <td />
                <td className="px-4 py-2.5 text-right text-sm font-bold text-teal-700 tabular-nums">{detail.total.toFixed(2)} DKK</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
