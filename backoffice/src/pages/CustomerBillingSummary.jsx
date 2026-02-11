import { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';

export default function CustomerBillingSummary() {
  const { t } = useTranslation();
  const { id } = useParams();
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    setLoading(true);
    setError(null);
    api.getCustomerBillingSummary(id)
      .then(setData)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [id]);

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-[3px] border-teal-100 border-t-teal-500 rounded-full animate-spin" />
          <p className="text-sm text-slate-400 font-medium">{t('billingSummary.loading')}</p>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-8">
        <div className="bg-rose-50 border border-rose-200 rounded-xl px-4 py-3 text-sm text-rose-600">{error}</div>
      </div>
    );
  }

  if (!data) {
    return (
      <div className="p-8">
        <p className="text-sm text-slate-500">{t('common.notFound')}</p>
      </div>
    );
  }

  const balance = data.totalPaid - data.totalBilled;
  const balancePositive = balance >= 0;

  return (
    <div className="p-4 sm:p-8 max-w-4xl mx-auto">
      {/* Back link */}
      <Link to={`/customers/${id}`} className="inline-flex items-center gap-1.5 text-sm text-slate-400 hover:text-teal-600 mb-4 transition-colors font-medium">
        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" d="M15.75 19.5 8.25 12l7.5-7.5" />
        </svg>
        {t('billingSummary.backToCustomer')}
      </Link>

      {/* Page header */}
      <div className="flex items-center gap-4 mb-6 animate-fade-in-up">
        <div className="w-14 h-14 rounded-2xl bg-teal-500 flex items-center justify-center shadow-lg shadow-teal-500/25">
          <span className="text-base font-bold text-white">
            {data.customerName.split(' ').map(n => n[0]).join('').slice(0, 2).toUpperCase()}
          </span>
        </div>
        <div>
          <h1 className="text-2xl sm:text-3xl font-bold text-slate-900 tracking-tight">{data.customerName}</h1>
          <p className="text-sm text-slate-500 font-medium">{t('billingSummary.title')}</p>
        </div>
      </div>

      {/* Stat cards */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-6 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <div className="bg-gradient-to-br from-white to-teal-50/30 rounded-xl p-5 shadow-sm border border-teal-100/50">
          <div className="text-sm font-medium text-teal-600 mb-1">{t('billingSummary.totalBilled')}</div>
          <div className="text-3xl font-bold text-teal-700">{data.totalBilled.toFixed(2)} DKK</div>
        </div>
        <div className="bg-gradient-to-br from-white to-emerald-50/30 rounded-xl p-5 shadow-sm border border-emerald-100/50">
          <div className="text-sm font-medium text-emerald-600 mb-1">{t('billingSummary.totalPaid')}</div>
          <div className="text-3xl font-bold text-emerald-700">{data.totalPaid.toFixed(2)} DKK</div>
        </div>
        <div className={`bg-gradient-to-br rounded-xl p-5 shadow-sm border ${
          balancePositive
            ? 'from-white to-emerald-50/30 border-emerald-100/50'
            : 'from-white to-rose-50/30 border-rose-100/50'
        }`}>
          <div className={`text-sm font-medium mb-1 ${balancePositive ? 'text-emerald-600' : 'text-rose-600'}`}>
            {t('billingSummary.balance')}
          </div>
          <div className={`text-3xl font-bold ${balancePositive ? 'text-emerald-700' : 'text-rose-700'}`}>
            {balancePositive ? '+' : ''}{balance.toFixed(2)} DKK
          </div>
        </div>
      </div>

      {/* Billing Periods */}
      <div className="bg-white rounded-2xl shadow-sm border border-slate-100 overflow-hidden mb-5 animate-fade-in-up" style={{ animationDelay: '120ms' }}>
        <div className="px-5 py-3.5 border-b border-slate-100 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <div className="w-1 h-4 rounded-full bg-teal-500" />
            <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">{t('billingSummary.billingPeriods')}</h3>
          </div>
          <span className="text-xs font-semibold text-teal-600 bg-teal-50 px-2.5 py-0.5 rounded-full">{data.billingPeriods.length}</span>
        </div>
        {data.billingPeriods.length === 0 ? (
          <div className="p-10 text-center">
            <p className="text-sm text-slate-400 font-medium">{t('billingSummary.noPeriods')}</p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[450px]">
              <thead>
                <tr className="border-b border-slate-50 bg-slate-50/50">
                  <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">{t('billingSummary.colPeriod')}</th>
                  <th className="text-right text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">{t('billingSummary.colAmount')}</th>
                  <th className="text-right text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">{t('billingSummary.colVat')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-50">
                {data.billingPeriods.map((bp, i) => (
                  <tr key={bp.billingPeriodId} className={`transition-colors duration-150 animate-slide-in ${i % 2 === 0 ? 'bg-white hover:bg-teal-50/30' : 'bg-slate-50 hover:bg-teal-50/50'}`} style={{ animationDelay: `${i * 40}ms` }}>
                    <td className="px-4 py-2.5">
                      <Link to={`/billing/periods/${bp.billingPeriodId}`} className="text-sm text-teal-600 hover:text-teal-700 font-medium">
                        {bp.periodStart} — {bp.periodEnd}
                      </Link>
                    </td>
                    <td className="px-4 py-2.5 text-right text-sm tabular-nums font-semibold text-slate-900">{bp.totalAmount.toFixed(2)} DKK</td>
                    <td className="px-4 py-2.5 text-right text-sm tabular-nums text-slate-600">{bp.totalVat.toFixed(2)} DKK</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Aconto Payments */}
      <div className="bg-white rounded-2xl shadow-sm border border-slate-100 overflow-hidden animate-fade-in-up" style={{ animationDelay: '180ms' }}>
        <div className="px-5 py-3.5 border-b border-slate-100 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <div className="w-1 h-4 rounded-full bg-teal-500" />
            <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">{t('billingSummary.acontoPayments')}</h3>
          </div>
          <span className="text-xs font-semibold text-teal-600 bg-teal-50 px-2.5 py-0.5 rounded-full">{data.acontoPayments.length}</span>
        </div>
        {data.acontoPayments.length === 0 ? (
          <div className="p-10 text-center">
            <p className="text-sm text-slate-400 font-medium">{t('billingSummary.noAconto')}</p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[350px]">
              <thead>
                <tr className="border-b border-slate-50 bg-slate-50/50">
                  <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">{t('billingSummary.colDate')}</th>
                  <th className="text-right text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">{t('billingSummary.colAmount')}</th>
                  <th className="text-left text-[10px] font-semibold text-slate-400 uppercase tracking-wider px-4 py-2">{t('billingSummary.colCurrency')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-50">
                {data.acontoPayments.map((ap, i) => (
                  <tr key={ap.id} className={`transition-colors duration-150 ${i % 2 === 0 ? 'bg-white' : 'bg-slate-50'}`}>
                    <td className="px-4 py-2.5 text-sm text-slate-700">{ap.paymentDate}</td>
                    <td className="px-4 py-2.5 text-right text-sm tabular-nums font-semibold text-slate-900">{ap.amount.toFixed(2)}</td>
                    <td className="px-4 py-2.5 text-sm text-slate-500">{ap.currency}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
