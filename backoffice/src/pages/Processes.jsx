import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';
import { ConversationTimeline } from './Messages';
import WattzonLoader from '../components/WattzonLoader';

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
  const [selectedStatus, setSelectedStatus] = useState('sent_to_datahub');
  const [data, setData] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [expandedId, setExpandedId] = useState(null);
  const [eventCache, setEventCache] = useState({});
  const [loadingEvents, setLoadingEvents] = useState(null);

  const fetchProcesses = useCallback((status) => {
    setLoading(true);
    setError(null);
    setExpandedId(null);
    api.getProcesses({ status })
      .then(setData)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    fetchProcesses(selectedStatus);
  }, [selectedStatus, fetchProcesses]);

  const toggleExpand = (id) => {
    if (expandedId === id) {
      setExpandedId(null);
      return;
    }
    setExpandedId(id);
    if (!eventCache[id]) {
      setLoadingEvents(id);
      api.getProcessEvents(id)
        .then((events) => {
          setEventCache((prev) => ({ ...prev, [id]: events }));
        })
        .catch(() => {
          setEventCache((prev) => ({ ...prev, [id]: [] }));
        })
        .finally(() => setLoadingEvents(null));
    }
  };

  const eventDotColor = (eventType) => {
    if (eventType === 'created') return 'bg-slate-400';
    if (eventType === 'completed') return 'bg-emerald-500';
    if (eventType === 'rejection_reason' || eventType === 'rejected') return 'bg-rose-500';
    if (eventType === 'cancelled') return 'bg-slate-400';
    if (eventType === 'cancellation_sent') return 'bg-amber-400';
    return 'bg-teal-500';
  };

  return (
    <div className="p-4 sm:p-8 max-w-6xl mx-auto">
      {/* Page header */}
      <div className="mb-6 animate-fade-in-up">
        <h1 className="text-2xl sm:text-3xl font-bold text-slate-900 tracking-tight">{t('processes.title')}</h1>
        <p className="text-base text-slate-500 mt-1">{t('processes.subtitle')}</p>
      </div>

      {/* Status filter bar */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-4 mb-6 animate-fade-in-up" style={{ animationDelay: '40ms' }}>
        <div className="flex flex-wrap gap-2">
          {STATUSES.map((s) => (
            <button
              key={s}
              onClick={() => setSelectedStatus(s)}
              className={`px-3 py-1.5 rounded-lg text-xs font-semibold transition-colors ${
                selectedStatus === s
                  ? 'bg-teal-500 text-white shadow-sm'
                  : 'bg-slate-100 text-slate-500 hover:bg-slate-200 cursor-pointer'
              }`}
            >
              {t('status.' + s)}
            </button>
          ))}
        </div>
      </div>

      {/* Stat cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 mb-6 animate-fade-in-up" style={{ animationDelay: '80ms' }}>
        <div className="bg-gradient-to-br from-white to-teal-50/30 rounded-xl p-5 shadow-sm border border-teal-100/50">
          <div className="text-sm font-medium text-teal-600 mb-1">{t('processes.processCount')}</div>
          <div className="text-3xl font-bold text-teal-700">{loading ? '...' : (data?.count ?? 0)}</div>
        </div>
        <div className="bg-gradient-to-br from-white to-slate-50 rounded-xl p-5 shadow-sm border border-slate-200">
          <div className="text-sm font-medium text-slate-500 mb-1">{t('processes.selectedStatus')}</div>
          <div className="mt-1">
            <span className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-semibold ${statusBadgeColor(selectedStatus)}`}>
              <span className={`w-1.5 h-1.5 rounded-full ${statusDotColor(selectedStatus)}`} />
              {t('status.' + selectedStatus)}
            </span>
          </div>
        </div>
      </div>

      {/* Process table */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden animate-fade-in-up" style={{ animationDelay: '120ms' }}>
        {error && (
          <div className="px-5 py-3 bg-rose-50 border-b border-rose-200">
            <p className="text-sm text-rose-600">{error}</p>
          </div>
        )}

        {loading ? (
          <WattzonLoader message={t('processes.loading')} />
        ) : !error && data?.processes?.length === 0 ? (
          <div className="px-6 py-12 text-center">
            <p className="text-sm text-slate-500">{t('processes.noProcesses')}</p>
          </div>
        ) : !error && data?.processes?.length > 0 ? (
          <div className="overflow-x-auto">
            <table className="w-full min-w-[750px]">
              <thead>
                <tr className="bg-slate-50 border-b border-slate-200">
                  <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('processes.colId')}</th>
                  <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('processes.colType')}</th>
                  <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('processes.colGsrn')}</th>
                  <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('processes.colStatus')}</th>
                  <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('processes.colEffectiveDate')}</th>
                  <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5">{t('processes.colCorrelation')}</th>
                  <th className="text-left text-[10px] font-semibold text-slate-600 uppercase tracking-wider px-4 py-2.5"></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {data.processes.map((p, i) => (
                  <>
                    <tr
                      key={p.id}
                      onClick={() => toggleExpand(p.id)}
                      className={`hover:bg-slate-50 transition-colors cursor-pointer animate-slide-in ${expandedId === p.id ? 'bg-slate-50' : ''}`}
                      style={{ animationDelay: `${i * 40}ms` }}
                    >
                      <td className="px-4 py-2.5">
                        <span className="font-mono text-sm text-teal-600 font-medium">{p.id.slice(0, 8)}</span>
                      </td>
                      <td className="px-4 py-2.5">
                        <span className={`inline-flex px-2 py-0.5 rounded-full text-[11px] font-medium ${processTypeBadge(p.processType)}`}>
                          {t('processType.' + p.processType) || p.processType}
                        </span>
                      </td>
                      <td className="px-4 py-2.5">
                        <span className="text-[11px] font-mono text-slate-500 bg-slate-100 px-1.5 py-0.5 rounded">{p.gsrn}</span>
                      </td>
                      <td className="px-4 py-2.5">
                        <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${statusBadgeColor(p.status)}`}>
                          <span className={`w-1.5 h-1.5 rounded-full ${statusDotColor(p.status)}`} />
                          {t('status.' + p.status)}
                        </span>
                      </td>
                      <td className="px-4 py-2.5 text-sm text-slate-700">{p.effectiveDate || <span className="text-slate-300">—</span>}</td>
                      <td className="px-4 py-2.5">
                        {p.datahubCorrelationId ? (
                          <span className="font-mono text-xs text-slate-400">{p.datahubCorrelationId.slice(0, 8)}</span>
                        ) : (
                          <span className="text-slate-300">—</span>
                        )}
                      </td>
                      <td className="px-4 py-2.5">
                        <Link
                          to={`/datahub/processes/${p.id}`}
                          onClick={(e) => e.stopPropagation()}
                          className="text-xs font-medium text-teal-600 hover:text-teal-700"
                        >
                          View
                        </Link>
                      </td>
                    </tr>
                    {expandedId === p.id && (
                      <tr key={`${p.id}-events`}>
                        <td colSpan={7} className="bg-slate-50/50 px-6 py-4 pl-12">
                          <div className="text-xs font-semibold text-slate-500 uppercase tracking-wider mb-3">{t('processes.processEvents')}</div>
                          {loadingEvents === p.id ? (
                            <div className="flex items-center gap-2 py-2">
                              <div className="w-4 h-4 border-2 border-teal-100 border-t-teal-500 rounded-full animate-spin" />
                              <span className="text-xs text-slate-400">{t('processes.loadingEvents')}</span>
                            </div>
                          ) : eventCache[p.id]?.length === 0 ? (
                            <p className="text-sm text-slate-400">{t('processes.noEvents')}</p>
                          ) : (
                            <div className="relative">
                              <div className="absolute left-[14px] top-2 bottom-2 w-px bg-teal-200" />
                              <div className="space-y-4">
                                {eventCache[p.id]?.map((evt, ei) => {
                                  let payloadContent = null;
                                  if (evt.payload) {
                                    try {
                                      const parsed = JSON.parse(evt.payload);
                                      payloadContent = parsed.reason || evt.payload;
                                    } catch {
                                      payloadContent = evt.payload;
                                    }
                                  }
                                  return (
                                    <div key={ei} className="flex gap-4 relative animate-slide-in" style={{ animationDelay: `${ei * 80}ms` }}>
                                      <div className={`w-7 h-7 rounded-full flex items-center justify-center shrink-0 z-10 ${
                                        ei === 0 ? `${eventDotColor(evt.eventType)} shadow-md shadow-teal-500/25` : 'bg-slate-100 border-2 border-slate-200'
                                      }`}>
                                        <div className={`w-2 h-2 rounded-full ${ei === 0 ? 'bg-white' : eventDotColor(evt.eventType)}`} />
                                      </div>
                                      <div className="pb-1 -mt-0.5">
                                        <div className="flex items-baseline gap-2">
                                          <span className="text-sm font-semibold text-slate-900">
                                            {t('event.' + evt.eventType) || evt.eventType}
                                          </span>
                                          {evt.source && evt.source !== 'system' && (
                                            <span className="text-[11px] text-slate-400 bg-slate-100 px-2 py-0.5 rounded-md font-medium">{evt.source}</span>
                                          )}
                                        </div>
                                        <p className="text-xs text-slate-400 mt-0.5 font-medium">
                                          {new Date(evt.occurredAt).toLocaleString('da-DK')}
                                        </p>
                                        {payloadContent && typeof payloadContent === 'string' && payloadContent !== evt.payload ? (
                                          <p className="text-xs text-slate-500 italic mt-1.5">{payloadContent}</p>
                                        ) : payloadContent ? (
                                          <pre className="text-xs text-slate-600 bg-slate-50 border border-slate-200 rounded-lg px-3 py-2 mt-2 overflow-x-auto">{payloadContent}</pre>
                                        ) : null}
                                      </div>
                                    </div>
                                  );
                                })}
                              </div>
                            </div>
                          )}
                          {p.datahubCorrelationId && (
                            <div className="mt-6">
                              <ConversationTimeline correlationId={p.datahubCorrelationId} />
                            </div>
                          )}
                        </td>
                      </tr>
                    )}
                  </>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </div>
    </div>
  );
}
