import { useState, useEffect } from 'react';
import { useParams, Link, useNavigate } from 'react-router-dom';
import { api } from '../api';
import { useTranslation } from '../i18n/LanguageContext';
import Breadcrumb from '../components/Breadcrumb';
import WattzonLoader from '../components/WattzonLoader';
import { shouldShowPendingStep, getPendingEventType } from '../utils/pendingStep';

const statusStyles = {
  registered:            { dot: 'bg-slate-400', badge: 'bg-slate-100 text-slate-600' },
  processing:            { dot: 'bg-teal-400', badge: 'bg-teal-50 text-teal-700' },
  awaiting_effectuation: { dot: 'bg-amber-400', badge: 'bg-amber-50 text-amber-700' },
  active:                { dot: 'bg-emerald-400', badge: 'bg-emerald-50 text-emerald-700' },
  rejected:   { dot: 'bg-rose-400', badge: 'bg-rose-50 text-rose-700' },
  cancellation_pending: { dot: 'bg-amber-400', badge: 'bg-amber-50 text-amber-700' },
  cancelled:  { dot: 'bg-slate-400', badge: 'bg-slate-100 text-slate-500' },
};

function StatusBadge({ status, label }) {
  const cfg = statusStyles[status] || statusStyles.registered;
  return (
    <span className={`inline-flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-semibold ${cfg.badge}`}>
      <span className={`w-1.5 h-1.5 rounded-full ${cfg.dot}`} />
      {label || status}
    </span>
  );
}

export default function SignupDetail() {
  const { t } = useTranslation();
  const { id } = useParams();
  const navigate = useNavigate();
  const [signup, setSignup] = useState(null);
  const [events, setEvents] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [cancelling, setCancelling] = useState(false);

  const tEvent = (type) => {
    const key = 'event.' + type;
    const val = t(key);
    return val === key ? type : val;
  };

  const tPending = (type) => {
    const key = 'event.pending.' + type;
    const val = t(key);
    return val === key ? '' : val;
  };

  useEffect(() => {
    setLoading(true);
    Promise.all([api.getSignup(id), api.getSignupEvents(id)])
      .then(([s, e]) => { setSignup(s); setEvents(e); })
      .catch((e) => setError(e.message))
      .finally(() => setLoading(false));
  }, [id]);

  useEffect(() => {
    if (!signup || !['registered', 'processing', 'awaiting_effectuation', 'cancellation_pending'].includes(signup.status)) return;
    const interval = setInterval(() => {
      Promise.all([api.getSignup(id), api.getSignupEvents(id)])
        .then(([s, e]) => { setSignup(s); setEvents(e); });
    }, 5000);
    return () => clearInterval(interval);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, signup?.status]);

  async function handleCancel() {
    if (!signup || !confirm(t('signupDetail.cancelConfirm'))) return;
    setCancelling(true);
    try {
      await api.cancelSignup(signup.signupNumber);
      const [s, e] = await Promise.all([api.getSignup(id), api.getSignupEvents(id)]);
      setSignup(s);
      setEvents(e);
    } catch (e) {
      setError(e.message);
    } finally {
      setCancelling(false);
    }
  }

  function handleCreateCorrected() {
    // Navigate to new signup with pre-fill data from this rejected signup
    const params = new URLSearchParams({
      correctedFromId: signup.id,
      correctedFromNumber: signup.signupNumber,
      darId: signup.darId,
      gsrn: signup.gsrn,
      productId: signup.productId,
      productName: signup.productName,
      customerName: signup.customerName,
      cprCvr: signup.cprCvr,
      contactType: signup.contactType,
      type: signup.type,
    });
    navigate(`/signups/new?${params.toString()}`);
  }

  if (loading) {
    return (
      <WattzonLoader message={t('signupDetail.loadingSignup')} />
    );
  }
  if (error) return <div className="p-8"><div className="bg-rose-50 border border-rose-200 rounded-xl px-4 py-3 text-sm text-rose-600">{error}</div></div>;
  if (!signup) return <div className="p-8"><p className="text-sm text-slate-500">{t('signupDetail.notFound')}</p></div>;

  const canCancel = ['registered', 'processing', 'awaiting_effectuation'].includes(signup.status);
  const correctionChain = signup.correctionChain || [];

  return (
    <div className="p-4 sm:p-8 max-w-3xl mx-auto">
      <Breadcrumb
        fallback={[{ label: t('signupList.title'), to: '/signups' }]}
        current={signup.signupNumber}
      />

      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 mb-6 animate-fade-in-up">
        <div className="flex items-center gap-3">
          <h1 className="text-2xl sm:text-3xl font-bold text-slate-900 tracking-tight">{signup.signupNumber}</h1>
          <StatusBadge status={signup.status} label={t('status.' + signup.status)} />
        </div>
        {canCancel && (
          <button
            onClick={handleCancel}
            disabled={cancelling}
            className="inline-flex items-center gap-1.5 px-4 py-2 bg-white text-rose-600 text-sm font-semibold rounded-xl border-2 border-rose-200 hover:bg-rose-50 hover:border-rose-300 disabled:opacity-40 transition-all"
          >
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18 18 6M6 6l12 12" />
            </svg>
            {cancelling ? t('signupDetail.cancelling') : t('common.cancel')}
          </button>
        )}
      </div>

      {/* Correction origin banner */}
      {signup.correctedFromId && signup.correctedFromSignupNumber && (
        <div className="mb-5 bg-teal-50 border border-teal-200 rounded-2xl p-4 flex items-center gap-3 animate-fade-in-up">
          <div className="w-8 h-8 rounded-lg bg-teal-500 flex items-center justify-center shrink-0">
            <svg className="w-4 h-4 text-white" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 12c0-1.232-.046-2.453-.138-3.662a4.006 4.006 0 0 0-3.7-3.7 48.678 48.678 0 0 0-7.324 0 4.006 4.006 0 0 0-3.7 3.7c-.017.22-.032.441-.046.662M19.5 12l3-3m-3 3-3-3m-12 3c0 1.232.046 2.453.138 3.662a4.006 4.006 0 0 0 3.7 3.7 48.656 48.656 0 0 0 7.324 0 4.006 4.006 0 0 0 3.7-3.7c.017-.22.032-.441.046-.662M4.5 12l3 3m-3-3-3 3" />
            </svg>
          </div>
          <div>
            <p className="text-sm font-semibold text-teal-800">{t('signupDetail.correctionOf')}</p>
            <p className="text-xs text-teal-600 mt-0.5">
              {t('signupDetail.correctsLink')}{' '}
              <Link to={`/signups/${signup.correctedFromId}`} className="font-semibold underline hover:text-teal-800">
                {signup.correctedFromSignupNumber}
              </Link>
            </p>
          </div>
        </div>
      )}

      {/* Rejection banner with create corrected button */}
      {signup.status === 'rejected' && signup.rejectionReason && (
        <div className="mb-6 bg-rose-50 border border-rose-200 rounded-2xl p-5 animate-fade-in-up">
          <div className="flex items-start gap-3">
            <div className="w-9 h-9 rounded-xl bg-rose-500 flex items-center justify-center shrink-0 shadow-md shadow-rose-500/20">
              <svg className="w-4 h-4 text-white" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126ZM12 15.75h.007v.008H12v-.008Z" />
              </svg>
            </div>
            <div className="flex-1">
              <p className="text-sm font-semibold text-rose-800">{t('signupDetail.rejectedByDatahub')}</p>
              <p className="text-sm text-rose-600 mt-1">{signup.rejectionReason}</p>
              <button
                onClick={handleCreateCorrected}
                className="inline-flex items-center gap-1.5 mt-3 px-4 py-2 bg-teal-500 text-white text-sm font-semibold rounded-xl shadow-md shadow-teal-500/20 hover:shadow-lg hover:-translate-y-0.5 transition-all duration-200"
              >
                {t('signupDetail.createCorrected')}
                <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 4.5 21 12m0 0-7.5 7.5M21 12H3" />
                </svg>
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Correction chain */}
      {correctionChain.length > 1 && (
        <div className="mb-5 bg-white rounded-2xl shadow-sm border border-slate-100 overflow-hidden animate-fade-in-up">
          <div className="px-5 py-3.5 border-b border-slate-100 flex items-center gap-2">
            <div className="w-1 h-4 rounded-full bg-teal-500" />
            <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">{t('signupDetail.correctionHistory')}</h3>
          </div>
          <div className="p-4">
            <div className="flex items-center gap-2 flex-wrap">
              {correctionChain.map((link, i) => (
                <div key={link.id} className="flex items-center gap-2">
                  {i > 0 && (
                    <svg className="w-4 h-4 text-slate-300 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 4.5 21 12m0 0-7.5 7.5M21 12H3" />
                    </svg>
                  )}
                  {link.id === signup.id ? (
                    <span className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-teal-50 text-teal-700 border-2 border-teal-300">
                      <StatusDot status={link.status} />
                      {link.signupNumber}
                    </span>
                  ) : (
                    <Link
                      to={`/signups/${link.id}`}
                      className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold bg-slate-50 text-slate-600 border border-slate-200 hover:border-teal-300 hover:text-teal-700 transition-colors"
                    >
                      <StatusDot status={link.status} />
                      {link.signupNumber}
                    </Link>
                  )}
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      <div className="bg-white rounded-2xl shadow-sm border border-slate-100 overflow-hidden mb-5 animate-fade-in-up" style={{ animationDelay: '60ms' }}>
        <div className="px-5 py-3.5 border-b border-slate-100 flex items-center gap-2">
          <div className="w-1 h-4 rounded-full bg-teal-500" />
          <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">{t('signupDetail.details')}</h3>
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-2 divide-y divide-slate-100">
          <Field label={t('signupDetail.type')} value={signup.type === 'move_in' ? t('signupDetail.typeMoveIn') : t('signupDetail.typeSwitch')} />
          <Field label={t('signupDetail.effectiveDate')} value={signup.effectiveDate} />
          <Field label={t('signupDetail.gsrn')} value={<span className="font-mono text-xs bg-slate-100 px-2 py-1 rounded-md text-slate-600 break-all">{signup.gsrn}</span>} />
          <Field label={t('signupDetail.darId')} value={<span className="font-mono text-xs text-slate-400 break-all">{signup.darId}</span>} />
          <Field label={t('signupDetail.product')} value={signup.productName} />
          <Field label={t('signupDetail.invoicingInterval')} value={<span className="capitalize">{signup.billingFrequency}</span>} />
          <Field label={t('signupDetail.created')} value={new Date(signup.createdAt).toLocaleString('da-DK')} />
        </div>
      </div>

      <div className="bg-white rounded-2xl shadow-sm border border-slate-100 overflow-hidden mb-5 animate-fade-in-up" style={{ animationDelay: '120ms' }}>
        <div className="px-5 py-3.5 border-b border-slate-100 flex items-center gap-2">
          <div className="w-1 h-4 rounded-full bg-teal-500" />
          <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">{t('signupDetail.customer')}</h3>
        </div>
        <div className="grid grid-cols-1 sm:grid-cols-2 divide-y divide-slate-100">
          <Field
            label={t('signupDetail.name')}
            value={
              <Link to={`/customers/${signup.customerId}?from=/signups/${id}`} className="font-semibold text-teal-600 hover:text-teal-800 transition-colors">
                {signup.customerName}
              </Link>
            }
          />
          <Field label={t('signupDetail.cprCvr')} value={<span className="font-mono text-xs bg-slate-100 px-2 py-1 rounded-md text-slate-500">{signup.cprCvr}</span>} />
          <Field label={t('signupDetail.contactType')} value={signup.contactType} />
        </div>
      </div>

      <div className="bg-white rounded-2xl shadow-sm border border-slate-100 overflow-hidden animate-fade-in-up" style={{ animationDelay: '180ms' }}>
        <div className="px-5 py-3.5 border-b border-slate-100 flex items-center gap-2">
          <div className="w-1 h-4 rounded-full bg-teal-500" />
          <h3 className="text-xs font-bold uppercase tracking-wider text-slate-500">{t('signupDetail.processTimeline')}</h3>
        </div>
        {events.length === 0 ? (
          <div className="p-10 text-center">
            <div className="w-12 h-12 rounded-2xl bg-slate-50 flex items-center justify-center mx-auto mb-2">
              <svg className="w-6 h-6 text-slate-300" fill="none" viewBox="0 0 24 24" strokeWidth={1} stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" d="M12 6v6h4.5m4.5 0a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z" />
              </svg>
            </div>
            <p className="text-sm text-slate-400 font-medium">{t('signupDetail.noEventsYet')}</p>
          </div>
        ) : (
          <div className="p-5">
            <div className="relative">
              <div className="absolute left-[14px] top-2 bottom-2 w-px bg-teal-200" />
              <div className="space-y-4">
                {events.map((evt, i) => {
                  const dotColor = evt.eventType === 'created' ? 'bg-slate-400'
                    : evt.eventType === 'completed' ? 'bg-emerald-500'
                    : evt.eventType === 'rejection_reason' ? 'bg-rose-500'
                    : evt.eventType === 'cancelled' ? 'bg-slate-400'
                    : evt.eventType === 'cancellation_sent' ? 'bg-amber-400'
                    : evt.eventType === 'cancellation_reason' ? 'bg-slate-400'
                    : 'bg-teal-500';
                  const isFirst = i === 0;
                  let payloadReason = null;
                  if (evt.payload) {
                    try {
                      const parsed = JSON.parse(evt.payload);
                      payloadReason = parsed.reason || null;
                    } catch { /* not JSON, ignore */ }
                  }
                  return (
                    <div key={i} className="flex gap-4 relative animate-slide-in" style={{ animationDelay: `${i * 80}ms` }}>
                      <div className={`w-7 h-7 rounded-full flex items-center justify-center shrink-0 z-10 ${
                        isFirst ? `${dotColor} shadow-md shadow-teal-500/25` : 'bg-slate-100 border-2 border-slate-200'
                      }`}>
                        <div className={`w-2 h-2 rounded-full ${isFirst ? 'bg-white' : dotColor}`} />
                      </div>
                      <div className="pb-1 -mt-0.5">
                        <div className="flex items-baseline gap-2">
                          <span className="text-sm font-semibold text-slate-900">
                            {tEvent(evt.eventType)}
                          </span>
                          {evt.source && evt.source !== 'system' && (
                            <span className="text-[11px] text-slate-400 bg-slate-100 px-2 py-0.5 rounded-md font-medium">{evt.source}</span>
                          )}
                        </div>
                        <p className="text-xs text-slate-400 mt-0.5 font-medium">
                          {new Date(evt.occurredAt).toLocaleString('da-DK')}
                        </p>
                        {payloadReason && (
                          <p className="text-xs text-slate-500 italic mt-1.5">{payloadReason}</p>
                        )}
                        {evt.payload && !payloadReason && (
                          <pre className="text-xs text-slate-600 bg-slate-50 border border-slate-200 rounded-lg px-3 py-2 mt-2 overflow-x-auto">{evt.payload}</pre>
                        )}
                      </div>
                    </div>
                  );
                })}
                {/* Pending step indicator */}
                {shouldShowPendingStep(events) && (
                  <div className="flex gap-4 relative animate-slide-in" style={{ animationDelay: `${events.length * 80}ms` }}>
                    <div className="w-7 h-7 rounded-full flex items-center justify-center shrink-0 z-10 bg-teal-100 border-2 border-teal-300">
                      <div className="w-2 h-2 rounded-full bg-teal-500 animate-pulse" />
                    </div>
                    <div className="pb-1 -mt-0.5">
                      <span className="text-sm font-medium text-teal-600">
                        {tPending(getPendingEventType(events))}
                      </span>
                    </div>
                  </div>
                )}
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

function StatusDot({ status }) {
  const cfg = statusStyles[status] || statusStyles.registered;
  return <span className={`w-1.5 h-1.5 rounded-full ${cfg.dot}`} />;
}

function Field({ label, value }) {
  return (
    <div className="px-5 py-3.5">
      <dt className="text-[11px] font-bold text-slate-400 uppercase tracking-wider">{label}</dt>
      <dd className="text-sm text-slate-700 mt-1 font-medium">{value}</dd>
    </div>
  );
}
