import { useState, useEffect } from 'react';
import { useParams, Link } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';
import Breadcrumb from '../components/Breadcrumb';
import { ConversationTimeline } from './Messages';

const statusStyles = {
  pending:               { dot: 'bg-slate-400', badge: 'bg-slate-100 text-slate-600' },
  sent_to_datahub:       { dot: 'bg-teal-400', badge: 'bg-teal-50 text-teal-700' },
  acknowledged:          { dot: 'bg-blue-400', badge: 'bg-blue-50 text-blue-700' },
  effectuation_pending:  { dot: 'bg-amber-400', badge: 'bg-amber-50 text-amber-700' },
  completed:             { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700' },
  rejected:              { dot: 'bg-rose-400', badge: 'bg-rose-50 text-rose-700' },
  cancellation_pending:  { dot: 'bg-amber-500', badge: 'bg-amber-50 text-amber-700' },
  cancelled:             { dot: 'bg-slate-400', badge: 'bg-slate-100 text-slate-500' },
};

const messageLabels = {
  'RSM-001': 'Acknowledgement',
  'RSM-005': 'End Notification',
  'RSM-022': 'Effectuation',
  'RSM-028': 'Customer Master Data',
  'RSM-031': 'Tariff Data',
};

const processTypeLabels = {
  supplier_switch: 'BRS-001',
  move_in: 'BRS-009',
  end_of_supply: 'BRS-002',
  move_out: 'BRS-010',
};

function StatusBadge({ status, label }) {
  const cfg = statusStyles[status] || statusStyles.pending;
  return (
    <span className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-semibold ${cfg.badge}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${cfg.dot}`} />
      {label || status}
    </span>
  );
}

function CheckIcon() {
  return (
    <svg className="w-5 h-5 text-emerald-500" fill="none" viewBox="0 0 24 24" strokeWidth={2.5} stroke="currentColor">
      <path strokeLinecap="round" strokeLinejoin="round" d="M4.5 12.75l6 6 9-13.5" />
    </svg>
  );
}

function XIcon() {
  return (
    <svg className="w-5 h-5 text-rose-500" fill="none" viewBox="0 0 24 24" strokeWidth={2.5} stroke="currentColor">
      <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
    </svg>
  );
}

function EmptyCircle() {
  return <div className="w-5 h-5 rounded-full border-2 border-slate-300" />;
}

function MessageStatusBadge({ status, received }) {
  if (!received) {
    return <span className="inline-flex px-2 py-0.5 rounded-full text-[11px] font-medium bg-slate-100 text-slate-500">Pending</span>;
  }
  if (status === 'dead_lettered') {
    return <span className="inline-flex px-2 py-0.5 rounded-full text-[11px] font-medium bg-rose-50 text-rose-700">Failed</span>;
  }
  return <span className="inline-flex px-2 py-0.5 rounded-full text-[11px] font-medium bg-emerald-50 text-emerald-700">Processed</span>;
}

export default function ProcessDetail() {
  const { t } = useTranslation();
  const { id } = useParams();
  const [detail, setDetail] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    setLoading(true);
    setError(null);
    api.getProcessDetail(id)
      .then(setDetail)
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [id]);

  if (loading) {
    return (
      <div className="p-4 sm:p-8 max-w-4xl mx-auto">
        <div className="flex items-center justify-center py-24">
          <div className="flex flex-col items-center gap-3">
            <div className="w-8 h-8 border-[3px] border-teal-100 border-t-teal-500 rounded-full animate-spin" />
            <p className="text-sm text-slate-400 font-medium">Loading process...</p>
          </div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-4 sm:p-8 max-w-4xl mx-auto">
        <div className="bg-rose-50 border border-rose-200 rounded-xl p-6 text-center">
          <p className="text-sm text-rose-600">{error}</p>
        </div>
      </div>
    );
  }

  if (!detail) return null;

  const receivedCount = detail.expectedMessages.filter((m) => m.received).length;
  const totalCount = detail.expectedMessages.length;
  const brsCode = processTypeLabels[detail.processType] || detail.processType;
  const showDataFlags = detail.processType === 'supplier_switch' || detail.processType === 'move_in';

  return (
    <div className="p-4 sm:p-8 max-w-4xl mx-auto">
      {/* Breadcrumb + header */}
      <div className="mb-6 animate-fade-in-up">
        <Breadcrumb
          fallback={[{ label: t('processes.title'), to: '/datahub/processes' }]}
          current={detail.id}
        />
        <div className="flex items-start justify-between flex-wrap gap-4">
          <div>
            <h1 className="text-2xl sm:text-3xl font-bold text-slate-900 tracking-tight">Process Detail</h1>
            <p className="text-sm text-slate-500 mt-1 font-mono">{detail.id}</p>
          </div>
          <StatusBadge status={detail.status} label={t('status.' + detail.status)} />
        </div>
      </div>

      {/* Process info card */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 p-5 mb-6 animate-fade-in-up" style={{ animationDelay: '40ms' }}>
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-4">
          <div>
            <div className="text-[10px] font-semibold text-slate-400 uppercase tracking-wider mb-1">Type</div>
            <div className="text-sm font-semibold text-slate-900">
              {t('processType.' + detail.processType) || detail.processType}
              <span className="ml-1.5 text-xs text-slate-400 font-normal">({brsCode})</span>
            </div>
          </div>
          <div>
            <div className="text-[10px] font-semibold text-slate-400 uppercase tracking-wider mb-1">GSRN</div>
            <div className="text-sm font-mono text-slate-700 bg-slate-50 px-2 py-0.5 rounded inline-block">{detail.gsrn}</div>
          </div>
          <div>
            <div className="text-[10px] font-semibold text-slate-400 uppercase tracking-wider mb-1">Effective Date</div>
            <div className="text-sm text-slate-700">{detail.effectiveDate || <span className="text-slate-300">&mdash;</span>}</div>
          </div>
          <div>
            <div className="text-[10px] font-semibold text-slate-400 uppercase tracking-wider mb-1">Correlation ID</div>
            {detail.datahubCorrelationId ? (
              <div className="text-sm font-mono text-slate-500">{detail.datahubCorrelationId.slice(0, 12)}...</div>
            ) : (
              <div className="text-sm text-slate-300">&mdash;</div>
            )}
          </div>
        </div>
        <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 mt-4 pt-4 border-t border-slate-100">
          <div>
            <div className="text-[10px] font-semibold text-slate-400 uppercase tracking-wider mb-1">Created</div>
            <div className="text-sm text-slate-600">{new Date(detail.createdAt).toLocaleString('da-DK')}</div>
          </div>
          <div>
            <div className="text-[10px] font-semibold text-slate-400 uppercase tracking-wider mb-1">Updated</div>
            <div className="text-sm text-slate-600">{new Date(detail.updatedAt).toLocaleString('da-DK')}</div>
          </div>
        </div>
      </div>

      {/* Expected Messages Checklist */}
      <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden mb-6 animate-fade-in-up" style={{ animationDelay: '80ms' }}>
        <div className="px-5 py-4 border-b border-slate-100 flex items-center justify-between">
          <h2 className="text-sm font-semibold text-slate-900">Expected Messages</h2>
          <span className={`text-xs font-semibold px-2.5 py-1 rounded-full ${
            receivedCount === totalCount && totalCount > 0
              ? 'bg-emerald-50 text-emerald-700'
              : 'bg-slate-100 text-slate-600'
          }`}>
            {receivedCount} / {totalCount} received
          </span>
        </div>

        {totalCount === 0 ? (
          <div className="px-5 py-8 text-center">
            <p className="text-sm text-slate-400">No expected messages defined for this process type.</p>
          </div>
        ) : (
          <div className="divide-y divide-slate-100">
            {detail.expectedMessages.map((msg) => (
              <div key={msg.messageType} className="flex items-center gap-4 px-5 py-3.5">
                <div className="shrink-0">
                  {msg.received && msg.status !== 'dead_lettered' ? <CheckIcon /> : msg.received && msg.status === 'dead_lettered' ? <XIcon /> : <EmptyCircle />}
                </div>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="text-sm font-semibold text-slate-900">{msg.messageType}</span>
                    <span className="text-xs text-slate-400">{messageLabels[msg.messageType] || ''}</span>
                  </div>
                  {msg.receivedAt && (
                    <p className="text-xs text-slate-400 mt-0.5">
                      Received {new Date(msg.receivedAt).toLocaleString('da-DK')}
                    </p>
                  )}
                </div>
                <div className="shrink-0">
                  <MessageStatusBadge status={msg.status} received={msg.received} />
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      {/* Data Completeness (BRS-001 / BRS-009 only) */}
      {showDataFlags && (
        <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden mb-6 animate-fade-in-up" style={{ animationDelay: '120ms' }}>
          <div className="px-5 py-4 border-b border-slate-100">
            <h2 className="text-sm font-semibold text-slate-900">Data Completeness</h2>
          </div>
          <div className="divide-y divide-slate-100">
            <div className="flex items-center gap-4 px-5 py-3.5">
              <div className="shrink-0">
                {detail.customerDataReceived ? <CheckIcon /> : <EmptyCircle />}
              </div>
              <div className="flex-1">
                <span className="text-sm font-medium text-slate-700">Customer Master Data</span>
              </div>
              <span className={`text-xs font-medium px-2 py-0.5 rounded-full ${
                detail.customerDataReceived ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-500'
              }`}>
                {detail.customerDataReceived ? 'Received' : 'Pending'}
              </span>
            </div>
            <div className="flex items-center gap-4 px-5 py-3.5">
              <div className="shrink-0">
                {detail.tariffDataReceived ? <CheckIcon /> : <EmptyCircle />}
              </div>
              <div className="flex-1">
                <span className="text-sm font-medium text-slate-700">Tariff Data</span>
              </div>
              <span className={`text-xs font-medium px-2 py-0.5 rounded-full ${
                detail.tariffDataReceived ? 'bg-emerald-50 text-emerald-700' : 'bg-slate-100 text-slate-500'
              }`}>
                {detail.tariffDataReceived ? 'Received' : 'Pending'}
              </span>
            </div>
          </div>
        </div>
      )}

      {/* Conversation Timeline */}
      {detail.datahubCorrelationId && (
        <div className="bg-white rounded-xl shadow-sm border border-slate-200 overflow-hidden animate-fade-in-up" style={{ animationDelay: '160ms' }}>
          <div className="px-5 py-4 border-b border-slate-100">
            <h2 className="text-sm font-semibold text-slate-900">Message Timeline</h2>
          </div>
          <div className="p-5">
            <ConversationTimeline correlationId={detail.datahubCorrelationId} />
          </div>
        </div>
      )}
    </div>
  );
}
