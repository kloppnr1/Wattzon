import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';

const PAGE_SIZE = 50;

const processStatusStyles = {
  pending: { dot: 'bg-amber-400', badge: 'bg-amber-50 text-amber-700' },
  sent_to_datahub: { dot: 'bg-orange-400', badge: 'bg-orange-50 text-orange-700' },
  acknowledged: { dot: 'bg-sky-400', badge: 'bg-sky-50 text-sky-700' },
  effectuation_pending: { dot: 'bg-blue-400', badge: 'bg-blue-50 text-blue-700' },
  completed: { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700' },
  rejected: { dot: 'bg-rose-400', badge: 'bg-rose-50 text-rose-700' },
  cancelled: { dot: 'bg-slate-400', badge: 'bg-slate-100 text-slate-600' },
};

function ProcessStatusBadge({ status }) {
  const { t } = useTranslation();
  const cfg = processStatusStyles[status] || { dot: 'bg-slate-400', badge: 'bg-slate-100 text-slate-600' };
  return (
    <span className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium ${cfg.badge}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${cfg.dot}`} />
      {t('status.' + status)}
    </span>
  );
}

function ProcessTypeBadge({ type }) {
  const { t } = useTranslation();
  const label = t('processType.' + type);
  const isSwitch = type?.includes('switch');
  const isMoveIn = type?.includes('move_in');
  const bg = isSwitch ? 'bg-indigo-50 text-indigo-700' : isMoveIn ? 'bg-violet-50 text-violet-700' : 'bg-slate-100 text-slate-600';
  return (
    <span className={`inline-flex px-2 py-0.5 rounded-full text-[11px] font-medium ${bg}`}>
      {label}
    </span>
  );
}

function TimelineEvent({ icon, label, time, color = 'teal' }) {
  return (
    <div className="flex items-center gap-3 py-2">
      <div className={`w-7 h-7 rounded-full bg-${color}-100 text-${color}-600 flex items-center justify-center text-xs font-bold flex-shrink-0`}>
        {icon}
      </div>
      <div className="flex-1 min-w-0">
        <div className="text-sm font-medium text-slate-700">{label}</div>
        <div className="text-xs text-slate-400">{time ? new Date(time).toLocaleString() : '-'}</div>
      </div>
    </div>
  );
}

function ConversationTimeline({ correlationId }) {
  const { t } = useTranslation();
  const [detail, setDetail] = useState(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    setLoading(true);
    api.getConversation(correlationId)
      .then(setDetail)
      .catch(() => {})
      .finally(() => setLoading(false));
  }, [correlationId]);

  if (loading) return <div className="p-4 text-sm text-slate-400">{t('messages.loadingTimeline')}</div>;
  if (!detail) return <div className="p-4 text-sm text-slate-400">{t('messages.noMessagesFound')}</div>;

  // Merge outbound + inbound into chronological timeline
  const events = [
    ...detail.outbound.map(o => ({ type: 'outbound', time: o.sentAt, data: o })),
    ...detail.inbound.map(i => ({ type: 'inbound', time: i.receivedAt, data: i })),
  ].sort((a, b) => new Date(a.time) - new Date(b.time));

  return (
    <div className="px-6 py-4 bg-slate-50 border-t border-slate-100">
      <div className="text-xs font-semibold text-slate-500 uppercase tracking-wider mb-3">{t('messages.conversationTimeline')}</div>
      <div className="space-y-1 relative">
        <div className="absolute left-[13px] top-4 bottom-4 w-px bg-slate-200" />
        {events.map((e, idx) => {
          if (e.type === 'outbound') {
            const o = e.data;
            return (
              <TimelineEvent
                key={`out-${idx}`}
                icon=">"
                color="teal"
                label={
                  <span>
                    <Link to={`/messages/outbound/${o.id}`} className="text-teal-600 hover:text-teal-700 font-medium">
                      {o.processType}
                    </Link>
                    {' '}{t('messages.sentToDatahub')}
                    {o.status === 'acknowledged_error' && <span className="text-rose-600 ml-1">{t('messages.errorSuffix')}</span>}
                  </span>
                }
                time={o.sentAt}
              />
            );
          } else {
            const i = e.data;
            const color = i.messageType === 'RSM-007' ? 'emerald' : i.messageType === 'RSM-009' ? 'sky' : 'slate';
            const desc = i.messageType === 'RSM-007' ? t('messages.activationConfirmed')
              : i.messageType === 'RSM-009' ? t('messages.acknowledgementReceived')
              : t('messages.received', { type: i.messageType });
            return (
              <TimelineEvent
                key={`in-${idx}`}
                icon="<"
                color={color}
                label={
                  <span>
                    <Link to={`/messages/inbound/${i.id}`} className="text-teal-600 hover:text-teal-700 font-medium">
                      {i.messageType}
                    </Link>
                    {' '}{desc}
                  </span>
                }
                time={i.receivedAt}
              />
            );
          }
        })}
      </div>
    </div>
  );
}

export default function Messages() {
  const { t } = useTranslation();
  const [tab, setTab] = useState('conversations');
  const [stats, setStats] = useState(null);

  // Conversations
  const [convData, setConvData] = useState(null);
  const [convPage, setConvPage] = useState(1);
  const [expandedCorr, setExpandedCorr] = useState(null);

  // Deliveries
  const [deliveries, setDeliveries] = useState(null);

  // Dead letters
  const [dlData, setDlData] = useState(null);
  const [dlPage, setDlPage] = useState(1);

  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    api.getMessageStats().then(setStats).catch(() => {});
  }, []);

  // Fetch conversations
  const fetchConversations = useCallback((p) => {
    setError(null);
    api.getConversations({ page: p, pageSize: PAGE_SIZE })
      .then(setConvData)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  // Fetch deliveries
  const fetchDeliveries = useCallback(() => {
    setError(null);
    api.getDataDeliveries()
      .then(setDeliveries)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  // Fetch dead letters
  const fetchDeadLetters = useCallback((p) => {
    setError(null);
    api.getDeadLetters({ page: p, pageSize: PAGE_SIZE })
      .then(setDlData)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    setLoading(true);
    if (tab === 'conversations') fetchConversations(convPage);
    else if (tab === 'deliveries') fetchDeliveries();
    else if (tab === 'dead-letters') fetchDeadLetters(dlPage);
  }, [tab, convPage, dlPage, fetchConversations, fetchDeliveries, fetchDeadLetters]);

  if (loading && !stats) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="flex flex-col items-center gap-3">
          <div className="w-8 h-8 border-[3px] border-teal-100 border-t-teal-500 rounded-full animate-spin" />
          <p className="text-sm text-slate-400 font-medium">{t('messages.loadingMessages')}</p>
        </div>
      </div>
    );
  }

  const convItems = convData?.items ?? [];
  const convTotal = convData?.totalCount ?? 0;
  const convPages = Math.ceil(convTotal / PAGE_SIZE);

  const dlItems = dlData?.items ?? [];
  const dlTotal = dlData?.totalCount ?? 0;
  const dlPages = Math.ceil(dlTotal / PAGE_SIZE);

  // Group deliveries by date
  const deliveryDates = deliveries ? [...new Set(deliveries.map(d => d.deliveryDate))].sort((a, b) => new Date(b) - new Date(a)) : [];

  return (
    <div className="p-4 sm:p-8 max-w-6xl mx-auto">
      {/* Page header */}
      <div className="mb-6">
        <h1 className="text-3xl font-bold text-slate-900 tracking-tight">{t('messages.title')}</h1>
        <p className="text-base text-slate-500 mt-1">{t('messages.subtitle')}</p>
      </div>

      {/* Stats cards */}
      {stats && (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 mb-6">
          <div className="bg-gradient-to-br from-white to-slate-50 rounded-xl p-5 shadow-sm border border-slate-100">
            <div className="text-sm font-medium text-slate-500 mb-1">{t('messages.totalMessages')}</div>
            <div className="text-3xl font-bold text-slate-900">{stats.totalInbound + stats.pendingOutbound}</div>
          </div>
          <div className="bg-gradient-to-br from-white to-teal-50/30 rounded-xl p-5 shadow-sm border border-teal-100/50">
            <div className="text-sm font-medium text-teal-600 mb-1">{t('messages.activeConversations')}</div>
            <div className="text-3xl font-bold text-teal-700">{convTotal}</div>
          </div>
          <div className="bg-gradient-to-br from-white to-emerald-50/30 rounded-xl p-5 shadow-sm border border-emerald-100/50">
            <div className="text-sm font-medium text-emerald-600 mb-1">{t('messages.processed')}</div>
            <div className="text-3xl font-bold text-emerald-700">{stats.processedCount}</div>
          </div>
          <div className="bg-gradient-to-br from-white to-rose-50/30 rounded-xl p-5 shadow-sm border border-rose-100/50">
            <div className="text-sm font-medium text-rose-600 mb-1">{t('messages.unresolvedDeadLetters')}</div>
            <div className="text-3xl font-bold text-rose-700">{stats.deadLetterCount}</div>
          </div>
        </div>
      )}

      {/* Tabs */}
      <div className="flex items-center gap-1 mb-5 bg-white rounded-xl p-1.5 w-fit max-w-full overflow-x-auto shadow-sm border border-slate-100">
        {[
          { key: 'conversations', label: t('messages.tabConversations') },
          { key: 'deliveries', label: t('messages.tabDeliveries') },
          { key: 'dead-letters', label: t('messages.tabDeadLetters') },
        ].map(({ key, label }) => (
          <button
            key={key}
            onClick={() => { setTab(key); setError(null); }}
            className={`px-4 py-2 text-sm font-semibold rounded-lg transition-all duration-200 ${
              tab === key
                ? 'bg-teal-500 text-white shadow-md'
                : 'text-slate-600 hover:text-slate-900 hover:bg-slate-50'
            }`}
          >
            {label}
          </button>
        ))}
      </div>

      {/* Content */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden">
        {error && (
          <div className="p-4 bg-rose-50 border-b border-rose-100 text-rose-700 text-sm">
            Error: {error}
          </div>
        )}

        {/* ── Conversations Tab ── */}
        {tab === 'conversations' && (
          <div className="overflow-x-auto">
            <table className="min-w-full divide-y divide-slate-200">
              <thead className="bg-slate-50">
                <tr>
                  <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('messages.colProcess')}</th>
                  <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('messages.colGsrn')}</th>
                  <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('messages.colCustomer')}</th>
                  <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('messages.colStatus')}</th>
                  <th className="px-6 py-3 text-center text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('messages.colMessages')}</th>
                  <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('messages.colLastActivity')}</th>
                </tr>
              </thead>
              <tbody className="bg-white divide-y divide-slate-100">
                {loading && convItems.length === 0 ? (
                  <tr><td colSpan="6" className="px-6 py-12 text-center text-slate-500">{t('common.loading')}</td></tr>
                ) : convItems.length === 0 ? (
                  <tr><td colSpan="6" className="px-6 py-12 text-center text-slate-500">{t('messages.noConversations')}</td></tr>
                ) : (
                  convItems.map((conv) => (
                    <>
                      <tr
                        key={conv.correlationId}
                        className="hover:bg-slate-50 transition-colors cursor-pointer"
                        onClick={() => setExpandedCorr(expandedCorr === conv.correlationId ? null : conv.correlationId)}
                      >
                        <td className="px-6 py-4 whitespace-nowrap">
                          <ProcessTypeBadge type={conv.processType} />
                        </td>
                        <td className="px-6 py-4 whitespace-nowrap text-sm font-mono text-slate-700">{conv.gsrn}</td>
                        <td className="px-6 py-4 whitespace-nowrap">
                          <div className="text-sm font-medium text-slate-900">{conv.customerName || '-'}</div>
                          {conv.signupNumber && <div className="text-xs text-slate-400">{conv.signupNumber}</div>}
                        </td>
                        <td className="px-6 py-4 whitespace-nowrap">
                          <ProcessStatusBadge status={conv.processStatus} />
                        </td>
                        <td className="px-6 py-4 whitespace-nowrap text-center">
                          <div className="flex items-center justify-center gap-2">
                            <span className="text-xs font-medium text-slate-500">{conv.outboundCount + conv.inboundCount}</span>
                            <div className="flex gap-0.5">
                              {conv.hasAcknowledgement && (
                                <span className="w-2 h-2 rounded-full bg-sky-400" title="Acknowledged (RSM-009)" />
                              )}
                              {conv.hasActivation && (
                                <span className="w-2 h-2 rounded-full bg-emerald-400" title="Activated (RSM-007)" />
                              )}
                            </div>
                          </div>
                        </td>
                        <td className="px-6 py-4 whitespace-nowrap text-sm text-slate-500">
                          {conv.lastReceivedAt ? new Date(conv.lastReceivedAt).toLocaleString()
                            : conv.firstSentAt ? new Date(conv.firstSentAt).toLocaleString()
                            : '-'}
                        </td>
                      </tr>
                      {expandedCorr === conv.correlationId && (
                        <tr key={`${conv.correlationId}-detail`}>
                          <td colSpan="6" className="p-0">
                            <ConversationTimeline correlationId={conv.correlationId} />
                          </td>
                        </tr>
                      )}
                    </>
                  ))
                )}
              </tbody>
            </table>

            {convPages > 1 && (
              <div className="px-6 py-4 bg-slate-50 border-t border-slate-200 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2">
                <div className="text-sm text-slate-600">
                  {t('common.showingRange', { from: (convPage - 1) * PAGE_SIZE + 1, to: Math.min(convPage * PAGE_SIZE, convTotal), total: convTotal })} {t('messages.showingConversations')}
                </div>
                <div className="flex gap-2">
                  <button
                    onClick={() => setConvPage(p => Math.max(1, p - 1))}
                    disabled={convPage === 1}
                    className="px-3 py-1.5 text-sm font-medium rounded-lg bg-white border border-slate-300 text-slate-700 hover:bg-slate-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                  >
                    {t('common.previous')}
                  </button>
                  <button
                    onClick={() => setConvPage(p => Math.min(convPages, p + 1))}
                    disabled={convPage === convPages}
                    className="px-3 py-1.5 text-sm font-medium rounded-lg bg-white border border-slate-300 text-slate-700 hover:bg-slate-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                  >
                    {t('common.next')}
                  </button>
                </div>
              </div>
            )}
          </div>
        )}

        {/* ── Data Deliveries Tab ── */}
        {tab === 'deliveries' && (
          <div className="divide-y divide-slate-200">
            {loading && !deliveries ? (
              <div className="px-6 py-12 text-center text-slate-500">{t('common.loading')}</div>
            ) : deliveryDates.length === 0 ? (
              <div className="px-6 py-12 text-center text-slate-500">{t('messages.noDeliveriesFound')}</div>
            ) : (
              deliveryDates.map((date) => {
                const dateStr = new Date(date).toLocaleDateString(undefined, { weekday: 'short', year: 'numeric', month: 'short', day: 'numeric' });
                const dayDeliveries = deliveries.filter(d => d.deliveryDate === date);
                const totalCount = dayDeliveries.reduce((sum, d) => sum + d.messageCount, 0);

                return (
                  <div key={date} className="px-6 py-4">
                    <div className="flex items-center justify-between mb-3">
                      <div className="text-sm font-semibold text-slate-900">{dateStr}</div>
                      <div className="text-xs text-slate-400">{t('messages.deliveryMessages', { count: totalCount })}</div>
                    </div>
                    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
                      {dayDeliveries.map((d) => {
                        const typeLabel = d.messageType === 'RSM-012' ? t('messages.meteringData')
                          : d.messageType === 'RSM-014' ? t('messages.aggregation')
                          : d.messageType === 'RSM-004' ? t('messages.gridChanges')
                          : d.messageType;
                        const typeColor = d.messageType === 'RSM-012' ? 'teal'
                          : d.messageType === 'RSM-014' ? 'indigo'
                          : 'amber';

                        return (
                          <div key={d.messageType} className="bg-slate-50 rounded-lg p-3">
                            <div className="flex items-center gap-2 mb-2">
                              <span className={`inline-flex px-1.5 py-0.5 rounded text-[10px] font-bold bg-${typeColor}-100 text-${typeColor}-700`}>
                                {d.messageType}
                              </span>
                              <span className="text-xs text-slate-500">{typeLabel}</span>
                            </div>
                            <div className="flex items-center gap-3">
                              <div>
                                <div className="text-lg font-bold text-slate-900">{d.messageCount}</div>
                                <div className="text-[10px] text-slate-400 uppercase">{t('messages.deliveryTotal')}</div>
                              </div>
                              <div>
                                <div className="text-lg font-bold text-emerald-600">{d.processedCount}</div>
                                <div className="text-[10px] text-slate-400 uppercase">{t('messages.deliveryProcessed')}</div>
                              </div>
                              {d.errorCount > 0 && (
                                <div>
                                  <div className="text-lg font-bold text-rose-600">{d.errorCount}</div>
                                  <div className="text-[10px] text-slate-400 uppercase">{t('messages.deliveryErrors')}</div>
                                </div>
                              )}
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  </div>
                );
              })
            )}
          </div>
        )}

        {/* ── Dead Letters Tab ── */}
        {tab === 'dead-letters' && (
          <>
            <div className="overflow-x-auto">
              <table className="min-w-full divide-y divide-slate-200">
                <thead className="bg-slate-50">
                  <tr>
                    <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('messages.colQueue')}</th>
                    <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('messages.colErrorReason')}</th>
                    <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('messages.colResolved')}</th>
                    <th className="px-6 py-3 text-left text-xs font-semibold text-slate-600 uppercase tracking-wider">{t('messages.colFailedAt')}</th>
                  </tr>
                </thead>
                <tbody className="bg-white divide-y divide-slate-100">
                  {loading && dlItems.length === 0 ? (
                    <tr><td colSpan="4" className="px-6 py-12 text-center text-slate-500">{t('common.loading')}</td></tr>
                  ) : dlItems.length === 0 ? (
                    <tr><td colSpan="4" className="px-6 py-12 text-center text-slate-500">{t('messages.noDeadLetters')}</td></tr>
                  ) : (
                    dlItems.map((dl) => (
                      <tr key={dl.id} className="hover:bg-slate-50 transition-colors">
                        <td className="px-6 py-4 whitespace-nowrap">
                          <Link to={`/messages/dead-letters/${dl.id}`} className="text-teal-600 font-medium hover:text-teal-700">
                            {dl.queueName}
                          </Link>
                        </td>
                        <td className="px-6 py-4 text-sm text-slate-700 max-w-md truncate">{dl.errorReason}</td>
                        <td className="px-6 py-4 whitespace-nowrap">
                          <span className={`inline-flex px-2 py-1 text-xs font-medium rounded-full ${
                            dl.resolved ? 'bg-emerald-100 text-emerald-700' : 'bg-rose-100 text-rose-700'
                          }`}>
                            {dl.resolved ? t('common.yes') : t('common.no')}
                          </span>
                        </td>
                        <td className="px-6 py-4 whitespace-nowrap text-sm text-slate-500">{new Date(dl.failedAt).toLocaleString()}</td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>

            {dlPages > 1 && (
              <div className="px-6 py-4 bg-slate-50 border-t border-slate-200 flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2">
                <div className="text-sm text-slate-600">
                  {t('common.showingRange', { from: (dlPage - 1) * PAGE_SIZE + 1, to: Math.min(dlPage * PAGE_SIZE, dlTotal), total: dlTotal })} {t('messages.showingDeadLetters')}
                </div>
                <div className="flex gap-2">
                  <button
                    onClick={() => setDlPage(p => Math.max(1, p - 1))}
                    disabled={dlPage === 1}
                    className="px-3 py-1.5 text-sm font-medium rounded-lg bg-white border border-slate-300 text-slate-700 hover:bg-slate-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                  >
                    {t('common.previous')}
                  </button>
                  <button
                    onClick={() => setDlPage(p => Math.min(dlPages, p + 1))}
                    disabled={dlPage === dlPages}
                    className="px-3 py-1.5 text-sm font-medium rounded-lg bg-white border border-slate-300 text-slate-700 hover:bg-slate-50 disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
                  >
                    {t('common.next')}
                  </button>
                </div>
              </div>
            )}
          </>
        )}
      </div>
    </div>
  );
}
