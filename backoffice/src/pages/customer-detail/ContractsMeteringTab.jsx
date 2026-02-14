import { useState, useCallback } from 'react';
import { api } from '../../api';
import { useTranslation } from '../../i18n/LanguageContext';

const chargeTypeBadge = {
  energy: 'bg-orange-50 text-orange-700',
  grid_tariff: 'bg-blue-50 text-blue-700',
  system_tariff: 'bg-indigo-50 text-indigo-700',
  transmission_tariff: 'bg-violet-50 text-violet-700',
  electricity_tax: 'bg-amber-50 text-amber-700',
  grid_subscription: 'bg-teal-50 text-teal-700',
  supplier_subscription: 'bg-emerald-50 text-emerald-700',
  production_credit: 'bg-lime-50 text-lime-700',
};

function getDefaultPeriod() {
  const now = new Date();
  const start = new Date(now.getFullYear(), now.getMonth() - 1, 1);
  const end = new Date(now.getFullYear(), now.getMonth(), 1);
  const fmt = (d) => d.toISOString().slice(0, 10);
  return { start: fmt(start), end: fmt(end) };
}

function SettlementPreviewPanel({ gsrn, t }) {
  const defaultPeriod = getDefaultPeriod();
  const [periodStart, setPeriodStart] = useState(defaultPeriod.start);
  const [periodEnd, setPeriodEnd] = useState(defaultPeriod.end);
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState(null);
  const [error, setError] = useState(null);

  const runPreview = useCallback(async () => {
    setLoading(true);
    setError(null);
    setResult(null);
    try {
      const data = await api.getSettlementPreview(gsrn, periodStart, periodEnd);
      setResult(data);
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  }, [gsrn, periodStart, periodEnd]);

  return (
    <div className="mt-3 bg-white rounded-lg border border-slate-200 overflow-hidden">
      <div className="px-4 py-3 border-b border-slate-100 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <div className="w-1 h-3.5 rounded-full bg-amber-400" />
          <h4 className="text-[10px] font-bold uppercase tracking-wider text-slate-500">{t('settlementPreview.title')}</h4>
        </div>
        <span className="text-[10px] text-slate-400 italic">{t('settlementPreview.dryRunNotice')}</span>
      </div>
      <div className="px-4 py-3">
        <div className="flex items-end gap-3 mb-3">
          <div>
            <label className="block text-[10px] font-semibold text-slate-500 uppercase tracking-wider mb-1">{t('settlementPreview.periodStart')}</label>
            <input type="date" value={periodStart} onChange={e => setPeriodStart(e.target.value)}
              className="border border-slate-200 rounded px-2 py-1 text-sm text-slate-700 focus:outline-none focus:ring-1 focus:ring-teal-400" />
          </div>
          <div>
            <label className="block text-[10px] font-semibold text-slate-500 uppercase tracking-wider mb-1">{t('settlementPreview.periodEnd')}</label>
            <input type="date" value={periodEnd} onChange={e => setPeriodEnd(e.target.value)}
              className="border border-slate-200 rounded px-2 py-1 text-sm text-slate-700 focus:outline-none focus:ring-1 focus:ring-teal-400" />
          </div>
          <button onClick={runPreview} disabled={loading}
            className="px-3 py-1.5 text-xs font-medium rounded bg-teal-600 text-white hover:bg-teal-700 disabled:opacity-50 transition-colors">
            {loading ? t('settlementPreview.calculating') : t('settlementPreview.calculate')}
          </button>
        </div>

        {error && (
          <div className="mb-3 px-3 py-2 bg-red-50 border border-red-200 rounded text-sm text-red-700">
            {t('settlementPreview.error')}: {error}
          </div>
        )}

        {result && (
          <div className="animate-fade-in-up">
            {/* Completeness indicator */}
            <div className="mb-3 flex items-center gap-2">
              <span className="text-[10px] font-semibold text-slate-500 uppercase tracking-wider">{t('settlementPreview.completeness')}:</span>
              <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${
                result.completeness.isComplete ? 'bg-emerald-50 text-emerald-700' : 'bg-amber-50 text-amber-700'
              }`}>
                <span className={`w-1.5 h-1.5 rounded-full ${result.completeness.isComplete ? 'bg-emerald-400' : 'bg-amber-400'}`} />
                {result.completeness.receivedHours} / {result.completeness.expectedHours} {t('settlementPreview.hoursReceived').split('{')[0].trim() || 'hours'}
              </span>
              <span className={`text-[11px] font-medium ${result.completeness.isComplete ? 'text-emerald-600' : 'text-amber-600'}`}>
                {result.completeness.isComplete ? t('settlementPreview.complete') : t('settlementPreview.incomplete')}
              </span>
            </div>

            {result.totalKwh === 0 ? (
              <div className="py-4 text-center text-sm text-slate-400">{t('settlementPreview.noData')}</div>
            ) : (
              <table className="w-full">
                <thead>
                  <tr className="bg-slate-50">
                    <th className="px-3 py-2 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('settlementPreview.chargeType')}</th>
                    <th className="px-3 py-2 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('settlementPreview.kwh')}</th>
                    <th className="px-3 py-2 text-right text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('settlementPreview.amount')}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {result.lines.map((line) => (
                    <tr key={line.chargeType} className="hover:bg-slate-50/50">
                      <td className="px-3 py-2">
                        <span className={`inline-flex px-2 py-0.5 rounded-full text-[11px] font-medium ${chargeTypeBadge[line.chargeType] || 'bg-slate-100 text-slate-600'}`}>
                          {t(`chargeType.${line.chargeType}`)}
                        </span>
                      </td>
                      <td className="px-3 py-2 text-right font-mono text-xs text-slate-600">
                        {line.kwh != null ? Number(line.kwh).toFixed(2) : '\u2014'}
                      </td>
                      <td className="px-3 py-2 text-right font-mono text-xs text-slate-700 font-medium">
                        {Number(line.amount).toFixed(2)}
                      </td>
                    </tr>
                  ))}
                </tbody>
                <tfoot className="border-t-2 border-slate-200">
                  <tr>
                    <td className="px-3 py-1.5 text-xs font-medium text-slate-500">{t('settlementPreview.subtotal')}</td>
                    <td />
                    <td className="px-3 py-1.5 text-right font-mono text-xs text-slate-700">{Number(result.subtotal).toFixed(2)}</td>
                  </tr>
                  <tr>
                    <td className="px-3 py-1.5 text-xs font-medium text-slate-500">{t('settlementPreview.vat')}</td>
                    <td />
                    <td className="px-3 py-1.5 text-right font-mono text-xs text-slate-700">{Number(result.vatAmount).toFixed(2)}</td>
                  </tr>
                  <tr className="bg-slate-50">
                    <td className="px-3 py-2 text-sm font-bold text-slate-800">{t('settlementPreview.total')}</td>
                    <td />
                    <td className="px-3 py-2 text-right font-mono text-sm font-bold text-slate-800">{Number(result.total).toFixed(2)} DKK</td>
                  </tr>
                </tfoot>
              </table>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

const tariffTypeLabels = {
  grid: 'tariffType.grid',
  system: 'tariffType.system',
  transmission: 'tariffType.transmission',
  electricity_tax: 'tariffType.electricity_tax',
  grid_subscription: 'tariffType.grid_subscription',
  supplier_subscription: 'tariffType.supplier_subscription',
};

const tariffTypeBadge = {
  grid: 'bg-blue-50 text-blue-700',
  system: 'bg-indigo-50 text-indigo-700',
  transmission: 'bg-violet-50 text-violet-700',
  electricity_tax: 'bg-amber-50 text-amber-700',
  grid_subscription: 'bg-teal-50 text-teal-700',
  supplier_subscription: 'bg-emerald-50 text-emerald-700',
};

function formatPrice(value) {
  if (value == null) return '\u2014';
  return Number(value).toFixed(4);
}

function PriceSummary({ item }) {
  // Hourly rates (grid, system, transmission)
  if (item.rates && item.rates.length > 0) {
    const prices = item.rates.map(r => Number(r.pricePerKwh));
    const min = Math.min(...prices);
    const max = Math.max(...prices);
    const allSame = min === max;
    return (
      <span className="font-mono text-xs text-slate-600">
        {allSame ? formatPrice(min) : `${formatPrice(min)} – ${formatPrice(max)}`}
        <span className="text-slate-400 ml-1">DKK/kWh</span>
      </span>
    );
  }

  // Monthly subscription
  if (item.amountPerMonth != null) {
    return (
      <span className="font-mono text-xs text-slate-600">
        {Number(item.amountPerMonth).toFixed(2)}
        <span className="text-slate-400 ml-1">DKK/md</span>
      </span>
    );
  }

  // Flat rate per kWh (electricity tax)
  if (item.ratePerKwh != null) {
    return (
      <span className="font-mono text-xs text-slate-600">
        {formatPrice(item.ratePerKwh)}
        <span className="text-slate-400 ml-1">DKK/kWh</span>
      </span>
    );
  }

  return <span className="text-slate-300">&mdash;</span>;
}

function HourlyRatesGrid({ rates, t }) {
  if (!rates || rates.length === 0) return null;

  const sorted = [...rates].sort((a, b) => a.hourNumber - b.hourNumber);
  const prices = sorted.map(r => Number(r.pricePerKwh));
  const max = Math.max(...prices);
  const min = Math.min(...prices);
  const range = max - min || 1;

  return (
    <div className="mt-2 bg-white rounded-lg border border-slate-200 p-3">
      <div className="text-[10px] font-semibold text-slate-500 uppercase tracking-wider mb-2">{t('customerDetail.hourlyRates')}</div>
      <div className="grid grid-cols-12 gap-1">
        {sorted.map(r => {
          const intensity = range > 0 ? (Number(r.pricePerKwh) - min) / range : 0.5;
          const bg = intensity > 0.66 ? 'bg-rose-100 text-rose-700'
            : intensity > 0.33 ? 'bg-amber-50 text-amber-700'
            : 'bg-emerald-50 text-emerald-700';
          return (
            <div key={r.hourNumber} className={`rounded px-1 py-1 text-center ${bg}`}>
              <div className="text-[9px] text-slate-400 font-medium">{String(r.hourNumber).padStart(2, '0')}</div>
              <div className="text-[10px] font-mono font-medium">{formatPrice(r.pricePerKwh)}</div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

export default function ContractsMeteringTab({ customer }) {
  const { t } = useTranslation();
  const [expandedMp, setExpandedMp] = useState(null);
  const [tariffs, setTariffs] = useState({});
  const [tariffsLoading, setTariffsLoading] = useState({});
  const [expandedTariff, setExpandedTariff] = useState(null);

  const productMap = {};
  if (customer.products) {
    customer.products.forEach(p => { productMap[p.id] = p.name; });
  }

  const toggleTariffs = (gsrn) => {
    if (expandedMp === gsrn) {
      setExpandedMp(null);
      return;
    }
    setExpandedMp(gsrn);
    if (!tariffs[gsrn]) {
      setTariffsLoading(prev => ({ ...prev, [gsrn]: true }));
      api.getMeteringPointTariffs(gsrn)
        .then(data => setTariffs(prev => ({ ...prev, [gsrn]: data })))
        .catch(() => setTariffs(prev => ({ ...prev, [gsrn]: [] })))
        .finally(() => setTariffsLoading(prev => ({ ...prev, [gsrn]: false })));
    }
  };

  return (
    <div className="space-y-5 animate-fade-in-up">
      {/* Contracts */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden">
        <div className="px-5 py-3.5 border-b border-slate-100 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <div className="w-1 h-4 rounded-full bg-teal-500" />
            <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">{t('customerDetail.contracts')}</h3>
          </div>
          <span className="text-xs font-semibold text-teal-600 bg-teal-50 px-2.5 py-0.5 rounded-full">{customer.contracts.length}</span>
        </div>
        {customer.contracts.length === 0 ? (
          <div className="p-10 text-center">
            <p className="text-sm text-slate-400 font-medium">{t('customerDetail.noContracts')}</p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[550px]">
              <thead>
                <tr className="bg-slate-50 border-b border-slate-200">
                  <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('customerDetail.colGsrn')}</th>
                  <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('customerDetail.colProduct')}</th>
                  <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('customerDetail.colBilling')}</th>
                  <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('customerDetail.colPayment')}</th>
                  <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('customerDetail.colStart')}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {customer.contracts.map((c, i) => (
                  <tr key={c.id} className="hover:bg-slate-50 transition-colors animate-slide-in" style={{ animationDelay: `${i * 40}ms` }}>
                    <td className="px-4 py-2.5">
                      <span className="text-[11px] font-mono text-slate-500 bg-slate-100 px-1.5 py-0.5 rounded">{c.gsrn}</span>
                    </td>
                    <td className="px-4 py-2.5 text-sm text-slate-700 font-medium">{productMap[c.productId] || <span className="text-slate-300">&mdash;</span>}</td>
                    <td className="px-4 py-2.5 text-sm text-slate-600 capitalize">{c.billingFrequency}</td>
                    <td className="px-4 py-2.5 text-sm text-slate-600 capitalize">{c.paymentModel}</td>
                    <td className="px-4 py-2.5 text-sm text-slate-500">{c.startDate}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Metering Points */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden">
        <div className="px-5 py-3.5 border-b border-slate-100 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <div className="w-1 h-4 rounded-full bg-teal-500" />
            <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">{t('customerDetail.meteringPoints')}</h3>
          </div>
          <span className="text-xs font-semibold text-teal-600 bg-teal-50 px-2.5 py-0.5 rounded-full">{customer.meteringPoints.length}</span>
        </div>
        {customer.meteringPoints.length === 0 ? (
          <div className="p-10 text-center">
            <p className="text-sm text-slate-400 font-medium">{t('customerDetail.noMeteringPoints')}</p>
          </div>
        ) : (
          <div className="divide-y divide-slate-100">
            {customer.meteringPoints.map((mp, i) => {
              const isExpanded = expandedMp === mp.gsrn;
              const mpTariffs = tariffs[mp.gsrn];
              const isLoading = tariffsLoading[mp.gsrn];

              return (
                <div key={mp.gsrn}>
                  <button
                    onClick={() => toggleTariffs(mp.gsrn)}
                    className="w-full text-left hover:bg-slate-50 transition-colors animate-slide-in"
                    style={{ animationDelay: `${i * 40}ms` }}
                  >
                    <div className="flex items-center px-4 py-2.5">
                      <div className="flex-shrink-0 w-44">
                        <span className="text-[11px] font-mono text-slate-500 bg-slate-100 px-1.5 py-0.5 rounded">{mp.gsrn}</span>
                      </div>
                      <div className="flex-shrink-0 w-16 text-sm text-slate-600">{mp.type}</div>
                      <div className="flex-shrink-0 w-16 text-sm text-slate-600">{mp.settlementMethod}</div>
                      <div className="flex-shrink-0 w-24 text-sm text-slate-600">{mp.gridAreaCode} <span className="text-slate-400">({mp.priceArea})</span></div>
                      <div className="flex-shrink-0 w-28">
                        <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${
                          mp.connectionStatus === 'connected' ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-500'
                        }`}>
                          <span className={`w-1.5 h-1.5 rounded-full ${mp.connectionStatus === 'connected' ? 'bg-emerald-400' : 'bg-slate-400'}`} />
                          {t('status.' + mp.connectionStatus)}
                        </span>
                      </div>
                      <div className="flex-1 text-sm text-slate-500">
                        {mp.supplyStart
                          ? `${mp.supplyStart}${mp.supplyEnd ? ` – ${mp.supplyEnd}` : ` – ${t('customerDetail.ongoing')}`}`
                          : <span className="text-slate-300">&mdash;</span>}
                      </div>
                      <div className="flex-shrink-0 ml-2">
                        <svg className={`w-4 h-4 text-slate-400 transition-transform ${isExpanded ? 'rotate-180' : ''}`} fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 8.25l-7.5 7.5-7.5-7.5" />
                        </svg>
                      </div>
                    </div>
                  </button>
                  {isExpanded && (
                    <div className="px-4 pb-4 bg-slate-50/50 border-t border-slate-100">
                      <div className="mt-3 mb-2 flex items-center gap-2">
                        <div className="w-1 h-3.5 rounded-full bg-indigo-400" />
                        <h4 className="text-[10px] font-bold uppercase tracking-wider text-slate-500">{t('customerDetail.priceElements')}</h4>
                      </div>
                      {isLoading ? (
                        <div className="py-6 text-center text-sm text-slate-400">{t('common.loading')}</div>
                      ) : !mpTariffs || mpTariffs.length === 0 ? (
                        <div className="py-6 text-center text-sm text-slate-400">{t('customerDetail.noPriceElements')}</div>
                      ) : (
                        <div className="bg-white rounded-lg border border-slate-200 overflow-hidden">
                          <table className="w-full">
                            <thead>
                              <tr className="bg-slate-50">
                                <th className="px-4 py-2 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('customerDetail.tariffColType')}</th>
                                <th className="px-4 py-2 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('customerDetail.tariffColId')}</th>
                                <th className="px-4 py-2 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('customerDetail.tariffColPrice')}</th>
                                <th className="px-4 py-2 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('customerDetail.tariffColValidFrom')}</th>
                                <th className="px-4 py-2 text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider">{t('customerDetail.tariffColValidTo')}</th>
                              </tr>
                            </thead>
                            <tbody className="divide-y divide-slate-100">
                              {mpTariffs.map((ta, idx) => {
                                const hasRates = ta.rates && ta.rates.length > 0;
                                const key = ta.id && ta.id !== '00000000-0000-0000-0000-000000000000' ? ta.id : `${ta.tariffType}-${idx}`;
                                const isRatesExpanded = expandedTariff === key;
                                return (
                                  <tr key={key}
                                    className={`transition-colors ${hasRates ? 'cursor-pointer hover:bg-slate-50' : ''}`}
                                    onClick={() => hasRates && setExpandedTariff(isRatesExpanded ? null : key)}
                                  >
                                    <td className="px-4 py-2">
                                      <span className={`inline-flex px-2 py-0.5 rounded-full text-[11px] font-medium ${tariffTypeBadge[ta.tariffType] || 'bg-slate-100 text-slate-600'}`}>
                                        {t(tariffTypeLabels[ta.tariffType] || `tariffType.${ta.tariffType}`)}
                                      </span>
                                    </td>
                                    <td className="px-4 py-2">
                                      {ta.tariffId
                                        ? <span className="font-mono text-xs text-slate-500">{ta.tariffId}</span>
                                        : <span className="text-slate-300">&mdash;</span>}
                                    </td>
                                    <td className="px-4 py-2">
                                      <div className="flex items-center gap-1.5">
                                        <PriceSummary item={ta} />
                                        {hasRates && (
                                          <svg className={`w-3.5 h-3.5 text-slate-400 transition-transform ${isRatesExpanded ? 'rotate-180' : ''}`} fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                                            <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 8.25l-7.5 7.5-7.5-7.5" />
                                          </svg>
                                        )}
                                      </div>
                                    </td>
                                    <td className="px-4 py-2 text-sm text-slate-600">{ta.validFrom}</td>
                                    <td className="px-4 py-2 text-sm text-slate-500">{ta.validTo || <span className="text-slate-300">&mdash;</span>}</td>
                                  </tr>
                                );
                              })}
                            </tbody>
                          </table>
                          {/* Hourly rates expanded view */}
                          {mpTariffs.map((ta, idx) => {
                            const key = ta.id && ta.id !== '00000000-0000-0000-0000-000000000000' ? ta.id : `${ta.tariffType}-${idx}`;
                            return expandedTariff === key && ta.rates && ta.rates.length > 0 && (
                              <div key={`rates-${key}`} className="px-4 pb-3 border-t border-slate-100">
                                <HourlyRatesGrid rates={ta.rates} t={t} />
                              </div>
                            );
                          })}
                        </div>
                      )}

                      {/* Settlement Preview */}
                      <SettlementPreviewPanel gsrn={mp.gsrn} t={t} />
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
