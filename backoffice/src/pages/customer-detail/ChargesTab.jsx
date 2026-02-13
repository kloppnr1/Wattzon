import { useState, useEffect } from 'react';
import { api } from '../../api';
import { useTranslation } from '../../i18n/LanguageContext';

export default function ChargesTab({ customerId }) {
  const { t } = useTranslation();
  const [summary, setSummary] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [expandedPeriod, setExpandedPeriod] = useState(null);
  const [lines, setLines] = useState({});
  const [linesLoading, setLinesLoading] = useState({});

  useEffect(() => {
    api.getCustomerBillingSummary(customerId)
      .then(setSummary)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [customerId]);

  const loadLines = (gsrn, periodId, fromDate, toDate) => {
    const key = `${gsrn}-${periodId}`;
    if (lines[key]) return;
    setLinesLoading(prev => ({ ...prev, [key]: true }));
    api.getMeteringPointLines(gsrn, { fromDate, toDate })
      .then(data => setLines(prev => ({ ...prev, [key]: data })))
      .catch(() => {})
      .finally(() => setLinesLoading(prev => ({ ...prev, [key]: false })));
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center py-12">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-[3px] border-teal-100 border-t-teal-500 rounded-full animate-spin" />
          <p className="text-sm text-slate-400 font-medium">{t('common.loading')}</p>
        </div>
      </div>
    );
  }

  if (error) return <div className="bg-rose-50 border border-rose-200 rounded-xl px-4 py-3 text-sm text-rose-600">{error}</div>;
  if (!summary) return <div className="p-8 text-center text-sm text-slate-400">{t('customerDetail.noCharges')}</div>;

  return (
    <div className="space-y-5 animate-fade-in-up">
      {/* Summary stat cards */}
      <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <div className="bg-gradient-to-br from-white to-teal-50/30 rounded-xl p-5 shadow-sm border border-teal-100/50">
          <div className="text-sm font-medium text-teal-600 mb-1">{t('customerDetail.chargesTotalBilled')}</div>
          <div className="text-3xl font-bold text-teal-700">{summary.totalBilled?.toFixed(2)} <span className="text-base font-medium">DKK</span></div>
        </div>
        <div className="bg-gradient-to-br from-white to-emerald-50/30 rounded-xl p-5 shadow-sm border border-emerald-100/50">
          <div className="text-sm font-medium text-emerald-600 mb-1">{t('customerDetail.chargesTotalPaid')}</div>
          <div className="text-3xl font-bold text-emerald-700">{summary.totalPaid?.toFixed(2)} <span className="text-base font-medium">DKK</span></div>
        </div>
        <div className={`bg-gradient-to-br rounded-xl p-5 shadow-sm border ${
          (summary.totalPaid - summary.totalBilled) >= 0
            ? 'from-white to-emerald-50/30 border-emerald-100/50'
            : 'from-white to-rose-50/30 border-rose-100/50'
        }`}>
          <div className={`text-sm font-medium mb-1 ${(summary.totalPaid - summary.totalBilled) >= 0 ? 'text-emerald-600' : 'text-rose-600'}`}>
            {t('customerDetail.chargesBalance')}
          </div>
          <div className={`text-3xl font-bold ${(summary.totalPaid - summary.totalBilled) >= 0 ? 'text-emerald-700' : 'text-rose-700'}`}>
            {(summary.totalPaid - summary.totalBilled) >= 0 ? '+' : ''}{(summary.totalPaid - summary.totalBilled).toFixed(2)} <span className="text-base font-medium">DKK</span>
          </div>
        </div>
      </div>

      {/* Billing periods */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden">
        <div className="px-5 py-3.5 border-b border-slate-100 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <div className="w-1 h-4 rounded-full bg-teal-500" />
            <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">{t('customerDetail.chargesPeriods')}</h3>
          </div>
          <span className="text-xs font-semibold text-teal-600 bg-teal-50 px-2.5 py-0.5 rounded-full">{summary.billingPeriods?.length ?? 0}</span>
        </div>
        {(!summary.billingPeriods || summary.billingPeriods.length === 0) ? (
          <div className="p-10 text-center">
            <p className="text-sm text-slate-400 font-medium">{t('customerDetail.noCharges')}</p>
          </div>
        ) : (
          <div className="divide-y divide-slate-100">
            {summary.billingPeriods.map((bp) => {
              const isExpanded = expandedPeriod === bp.billingPeriodId;
              return (
                <div key={bp.billingPeriodId}>
                  <button
                    onClick={() => setExpandedPeriod(isExpanded ? null : bp.billingPeriodId)}
                    className="w-full px-5 py-3.5 flex items-center justify-between hover:bg-slate-50 transition-colors text-left"
                  >
                    <div>
                      <span className="text-sm font-medium text-slate-700">{bp.periodStart} — {bp.periodEnd}</span>
                    </div>
                    <div className="flex items-center gap-4">
                      <span className="text-sm tabular-nums font-semibold text-slate-900">{bp.totalAmount?.toFixed(2)} DKK</span>
                      <span className="text-xs tabular-nums text-slate-400">{t('customerDetail.chargesVat')}: {bp.totalVat?.toFixed(2)}</span>
                      <svg className={`w-4 h-4 text-slate-400 transition-transform ${isExpanded ? 'rotate-180' : ''}`} fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 8.25l-7.5 7.5-7.5-7.5" />
                      </svg>
                    </div>
                  </button>
                  {isExpanded && (
                    <div className="px-5 pb-4 bg-slate-50/50">
                      <p className="text-xs text-slate-500 mb-3">{t('customerDetail.chargesClickGsrn')}</p>
                      {/* Show lines per GSRN for this period — use metering points from summary or load them */}
                      <MeteringPointLines
                        t={t}
                        bp={bp}
                        lines={lines}
                        linesLoading={linesLoading}
                        loadLines={loadLines}
                      />
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Aconto payments */}
      {summary.acontoPayments && summary.acontoPayments.length > 0 && (
        <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden">
          <div className="px-5 py-3.5 border-b border-slate-100 flex items-center justify-between">
            <div className="flex items-center gap-2">
              <div className="w-1 h-4 rounded-full bg-teal-500" />
              <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">{t('customerDetail.acontoPayments')}</h3>
            </div>
            <span className="text-xs font-semibold text-teal-600 bg-teal-50 px-2.5 py-0.5 rounded-full">{summary.acontoPayments.length}</span>
          </div>
          <div className="overflow-x-auto">
            <table className="w-full">
              <thead>
                <tr className="bg-slate-50 border-b border-slate-200">
                  <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('customerDetail.acontoColDate')}</th>
                  <th className="text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('customerDetail.acontoColAmount')}</th>
                  <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('customerDetail.acontoColCurrency')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {summary.acontoPayments.map((ap) => (
                  <tr key={ap.id} className="hover:bg-slate-50 transition-colors">
                    <td className="px-4 py-2.5 text-sm text-slate-700">{ap.paymentDate}</td>
                    <td className="px-4 py-2.5 text-right text-sm tabular-nums font-semibold text-slate-900">{ap.amount?.toFixed(2)}</td>
                    <td className="px-4 py-2.5 text-sm text-slate-500">{ap.currency}</td>
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

function MeteringPointLines({ t, bp, lines, linesLoading, loadLines }) {
  const [expandedGsrn, setExpandedGsrn] = useState(null);

  // If the billing period has gsrn info, use it; otherwise we just show the period total
  const gsrnList = bp.gsrnList || [];

  if (gsrnList.length === 0) {
    return (
      <div className="text-sm text-slate-400">{t('customerDetail.chargesNoBreakdown')}</div>
    );
  }

  return (
    <div className="space-y-2">
      {gsrnList.map((gsrn) => {
        const key = `${gsrn}-${bp.billingPeriodId}`;
        const isOpen = expandedGsrn === gsrn;
        const lineData = lines[key];
        const isLoading = linesLoading[key];

        return (
          <div key={gsrn} className="bg-white rounded-lg border border-slate-200 overflow-hidden">
            <button
              onClick={() => {
                setExpandedGsrn(isOpen ? null : gsrn);
                if (!isOpen) loadLines(gsrn, bp.billingPeriodId, bp.periodStart, bp.periodEnd);
              }}
              className="w-full px-4 py-2.5 flex items-center justify-between hover:bg-slate-50 transition-colors text-left"
            >
              <span className="text-[11px] font-mono text-slate-500 bg-slate-100 px-1.5 py-0.5 rounded">{gsrn}</span>
              <svg className={`w-3.5 h-3.5 text-slate-400 transition-transform ${isOpen ? 'rotate-180' : ''}`} fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 8.25l-7.5 7.5-7.5-7.5" />
              </svg>
            </button>
            {isOpen && (
              <div className="border-t border-slate-100">
                {isLoading ? (
                  <div className="px-4 py-6 text-center text-sm text-slate-400">{t('common.loading')}</div>
                ) : !lineData || (Array.isArray(lineData) && lineData.length === 0) ? (
                  <div className="px-4 py-6 text-center text-sm text-slate-400">{t('customerDetail.chargesNoLines')}</div>
                ) : (
                  <table className="w-full">
                    <thead>
                      <tr className="bg-slate-50">
                        <th className="px-4 py-2 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('customerDetail.chargesColType')}</th>
                        <th className="px-4 py-2 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('customerDetail.chargesColKwh')}</th>
                        <th className="px-4 py-2 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('customerDetail.chargesColAmount')}</th>
                        <th className="px-4 py-2 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('customerDetail.chargesColVat')}</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                      {(Array.isArray(lineData) ? lineData : lineData.items || []).map((line, i) => (
                        <tr key={i} className="hover:bg-slate-50 transition-colors">
                          <td className="px-4 py-2 text-sm text-slate-700">{t(`chargeType.${line.chargeType}`)}</td>
                          <td className="px-4 py-2 text-right text-sm tabular-nums text-slate-600">{line.totalKwh?.toFixed(3)}</td>
                          <td className="px-4 py-2 text-right text-sm tabular-nums font-semibold text-slate-900">{line.amount?.toFixed(2)}</td>
                          <td className="px-4 py-2 text-right text-sm tabular-nums text-slate-500">{line.vat?.toFixed(2)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}
