import { useState, useEffect, useRef } from 'react';
import { useNavigate, Link, useSearchParams } from 'react-router-dom';
import { api } from '../api';
import { useDawaSearch } from '../hooks/useDawaSearch';
import { useTranslation } from '../i18n/LanguageContext';

function tomorrow() {
  const d = new Date();
  d.setDate(d.getDate() + 1);
  return d.toISOString().split('T')[0];
}

const INPUT = 'w-full rounded-lg border border-slate-200 bg-white px-4 py-2.5 text-sm text-slate-800 placeholder:text-slate-300 shadow-sm focus:outline-none focus:border-teal-500 focus:ring-2 focus:ring-teal-500/20 transition-all';

export default function SignupNew() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const [products, setProducts] = useState([]);
  const [error, setError] = useState(null);
  const [submitting, setSubmitting] = useState(false);

  // Correction context
  const correctedFromId = searchParams.get('correctedFromId');
  const correctedFromNumber = searchParams.get('correctedFromNumber');

  // ── Row 1: Process + Effective Date ──
  const [type, setType] = useState('switch');
  const [effectiveDate, setEffectiveDate] = useState(tomorrow());

  // ── Row 2: Metering Point ──
  const [addressMode, setAddressMode] = useState('search');
  const [addressQuery, setAddressQuery] = useState('');
  const [selectedAddress, setSelectedAddress] = useState(null);
  const [showSuggestions, setShowSuggestions] = useState(false);
  const [darId, setDarId] = useState('');
  const [gsrnInput, setGsrnInput] = useState('');
  const [meteringPoints, setMeteringPoints] = useState(null);
  const [selectedGsrn, setSelectedGsrn] = useState(null);
  const [lookingUp, setLookingUp] = useState(false);
  const suggestionsRef = useRef(null);

  const { results: addressSuggestions, loading: searchingAddress } = useDawaSearch(addressQuery, {
    enabled: addressMode === 'search' && !selectedAddress,
  });

  // ── Row 3: Product ──
  const [selectedProduct, setSelectedProduct] = useState(null);

  // ── Row 4: Customer ──
  const [contactType, setContactType] = useState('private');
  const [customerName, setCustomerName] = useState('');
  const [cprCvr, setCprCvr] = useState('');
  const [email, setEmail] = useState('');
  const [mobile, setMobile] = useState('');
  const [phone, setPhone] = useState('');

  // Billing address
  const [billingSameAsSupply, setBillingSameAsSupply] = useState(true);
  const [billingAddressQuery, setBillingAddressQuery] = useState('');
  const [selectedBillingAddress, setSelectedBillingAddress] = useState(null);
  const [showBillingSuggestions, setShowBillingSuggestions] = useState(false);
  const billingSuggestionsRef = useRef(null);

  const { results: billingSuggestions, loading: searchingBillingAddress } = useDawaSearch(billingAddressQuery, {
    enabled: !billingSameAsSupply && !selectedBillingAddress,
  });

  // ── Pre-fill from correction params ──
  const prefilled = useRef(false);
  useEffect(() => {
    if (prefilled.current) return;
    prefilled.current = true;

    const pfDarId = searchParams.get('darId');
    const pfGsrn = searchParams.get('gsrn');
    const pfProductId = searchParams.get('productId');
    const pfCustomerName = searchParams.get('customerName');
    const pfCprCvr = searchParams.get('cprCvr');
    const pfContactType = searchParams.get('contactType');
    const pfType = searchParams.get('type');

    if (pfDarId && pfGsrn) {
      setAddressMode('dar');
      setDarId(pfDarId);
      setSelectedGsrn(pfGsrn);
      setMeteringPoints([{ gsrn: pfGsrn, type: 'E17', grid_area_code: '—' }]);
    }
    if (pfProductId) setSelectedProduct(pfProductId);
    if (pfCustomerName) setCustomerName(pfCustomerName);
    if (pfCprCvr) setCprCvr(pfCprCvr);
    if (pfContactType) setContactType(pfContactType);
    if (pfType) {
      setType(pfType);
      if (pfType === 'switch') setEffectiveDate(tomorrow());
      else setEffectiveDate('');
    }
  }, [searchParams]);

  // ── Auto-set effective date when process type changes ──
  useEffect(() => {
    if (type === 'switch') setEffectiveDate(tomorrow());
    else setEffectiveDate('');
  }, [type]);

  // ── Click-outside handlers ──
  useEffect(() => {
    function handleClick(e) {
      if (suggestionsRef.current && !suggestionsRef.current.contains(e.target)) {
        setShowSuggestions(false);
      }
      if (billingSuggestionsRef.current && !billingSuggestionsRef.current.contains(e.target)) {
        setShowBillingSuggestions(false);
      }
    }
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, []);

  // ── Load products ──
  useEffect(() => {
    api.getProducts().then(setProducts).catch(() => {});
  }, []);

  // ── Address helpers ──
  function selectAddress(suggestion) {
    setSelectedAddress(suggestion);
    setAddressQuery(suggestion.text);
    setDarId(suggestion.darId);
    setShowSuggestions(false);
    lookupGsrn(suggestion.darId);
  }

  function clearAddress() {
    setSelectedAddress(null);
    setAddressQuery('');
    setDarId('');
    setGsrnInput('');
    setMeteringPoints(null);
    setSelectedGsrn(null);
  }

  async function lookupGsrn(id) {
    setLookingUp(true);
    setError(null);
    setMeteringPoints(null);
    setSelectedGsrn(null);
    try {
      const result = await api.lookupAddress(id);
      setMeteringPoints(result);
      const available = result.filter((mp) => !mp.has_active_process);
      if (available.length === 1) setSelectedGsrn(available[0].gsrn);
      else if (result.length === 1 && !result[0].has_active_process) setSelectedGsrn(result[0].gsrn);
    } catch (e) {
      setError(e.message);
    } finally {
      setLookingUp(false);
    }
  }

  async function validateGsrn(gsrn) {
    setLookingUp(true);
    setError(null);
    setMeteringPoints(null);
    setSelectedGsrn(null);
    setDarId('');
    try {
      const result = await api.validateGsrn(gsrn);
      setMeteringPoints(result);
      const available = result.filter((mp) => !mp.has_active_process);
      if (available.length === 1) setSelectedGsrn(available[0].gsrn);
    } catch (e) {
      setError(e.message);
    } finally {
      setLookingUp(false);
    }
  }

  // ── CPR/CVR validation ──
  const cprCvrLabel = contactType === 'private' ? t('signupNew.cpr') : t('signupNew.cvr');
  const cprCvrExpectedLength = contactType === 'private' ? 10 : 8;
  const cprCvrValid = cprCvr.length === 0 || (/^\d+$/.test(cprCvr) && cprCvr.length === cprCvrExpectedLength);
  const cprCvrComplete = /^\d+$/.test(cprCvr) && cprCvr.length === cprCvrExpectedLength;

  // ── Form validity ──
  const canSubmit =
    selectedGsrn &&
    selectedProduct &&
    customerName.trim() &&
    cprCvrComplete &&
    email.trim() &&
    mobile.trim() &&
    effectiveDate &&
    !submitting;

  // ── Submit ──
  async function handleSubmit() {
    setSubmitting(true);
    setError(null);
    try {
      const addr = billingSameAsSupply && selectedAddress
        ? selectedAddress
        : selectedBillingAddress;
      const payload = {
        darId,
        gsrn: selectedGsrn,
        customerName,
        cprCvr,
        contactType,
        email,
        phone,
        mobile,
        productId: selectedProduct,
        type,
        effectiveDate,
        billingDarId: addr?.darId || undefined,
        billingStreet: addr?.street || undefined,
        billingHouseNumber: addr?.houseNumber || undefined,
        billingFloor: addr?.floor || undefined,
        billingDoor: addr?.door || undefined,
        billingPostalCode: addr?.postalCode || undefined,
        billingCity: addr?.city || undefined,
      };
      if (correctedFromId) {
        payload.correctedFromId = correctedFromId;
      }
      const result = await api.createSignup(payload);
      navigate(`/signups/${result.id}`);
    } catch (e) {
      setError(e.message);
      setSubmitting(false);
    }
  }

  const selectedProductObj = products.find((p) => p.id === selectedProduct);

  return (
    <div className="min-h-full bg-slate-50">
      <div className="px-3 sm:px-6 py-8 max-w-4xl mx-auto">

        {/* Breadcrumb */}
        <Link to="/signups" className="group inline-flex items-center gap-2 text-sm text-slate-400 hover:text-teal-600 mb-6 transition-colors font-medium">
          <svg className="w-4 h-4 transition-transform group-hover:-translate-x-0.5" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M15.75 19.5 8.25 12l7.5-7.5" />
          </svg>
          {t('signupNew.backToSignups')}
        </Link>

        {/* Header */}
        <div className="mb-6">
          <h1 className="text-2xl sm:text-3xl font-extrabold text-slate-900 tracking-tight">
            {correctedFromId ? t('signupNew.titleCorrected') : t('signupNew.title')}
          </h1>
        </div>

        {/* Correction banner */}
        {correctedFromId && correctedFromNumber && (
          <div className="mb-6 border-l-4 border-teal-500 bg-teal-50/60 rounded-r-lg px-5 py-4 flex items-center gap-3">
            <svg className="w-5 h-5 text-teal-600 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 12c0-1.232-.046-2.453-.138-3.662a4.006 4.006 0 0 0-3.7-3.7 48.678 48.678 0 0 0-7.324 0 4.006 4.006 0 0 0-3.7 3.7c-.017.22-.032.441-.046.662M19.5 12l3-3m-3 3-3-3m-12 3c0 1.232.046 2.453.138 3.662a4.006 4.006 0 0 0 3.7 3.7 48.656 48.656 0 0 0 7.324 0 4.006 4.006 0 0 0 3.7-3.7c.017-.22.032-.441.046-.662M4.5 12l3 3m-3-3-3 3" />
            </svg>
            <p className="text-sm text-teal-800 font-semibold">
              {t('signupNew.correctionBanner', { number: correctedFromNumber })}
            </p>
          </div>
        )}

        {/* Error banner */}
        {error && (
          <div className="mb-6 border-l-4 border-rose-500 bg-rose-50/60 rounded-r-lg px-5 py-4 flex items-start gap-3">
            <svg className="w-5 h-5 text-rose-500 shrink-0 mt-0.5" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
              <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 3.75h.008v.008H12v-.008Z" />
            </svg>
            <p className="text-sm text-rose-700 font-medium">{error}</p>
          </div>
        )}

        {/* ══════ Main form card ══════ */}
        <div className="bg-white rounded-2xl shadow-sm border border-slate-100">
          <div className="p-4 sm:p-8 space-y-5">

            {/* ── PROCESS ── */}
            <SectionDivider label={t('signupNew.processType')} />

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">{t('signupNew.processType')}</label>
                <select value={type} onChange={(e) => setType(e.target.value)} className={INPUT}>
                  <option value="switch">{t('signupNew.supplierSwitch')}</option>
                  <option value="move_in">{t('signupNew.moveIn')}</option>
                </select>
              </div>
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">{t('signupNew.effectiveDate')}</label>
                <input type="date" value={effectiveDate} onChange={(e) => setEffectiveDate(e.target.value)} className={INPUT} />
              </div>
            </div>

            {/* ── METERING POINT ── */}
            <SectionDivider label={t('signupNew.gsrn')} />

            {/* Mode toggle pills */}
            <div className="flex flex-wrap gap-2">
              {['search', 'dar', 'gsrn'].map((mode) => (
                <button
                  key={mode}
                  onClick={() => { setAddressMode(mode); clearAddress(); }}
                  className={`px-4 py-2 text-xs font-bold rounded-lg border-2 transition-all duration-200 ${
                    addressMode === mode
                      ? 'bg-teal-600 text-white border-teal-600 shadow-sm'
                      : 'bg-white text-slate-500 border-slate-200 hover:border-slate-300 hover:text-slate-700'
                  }`}
                >
                  {mode === 'search' ? t('signupNew.searchAddress') : mode === 'dar' ? t('signupNew.darId') : t('signupNew.gsrn')}
                </button>
              ))}
            </div>

            {/* Search mode */}
            {addressMode === 'search' && (
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">{t('signupNew.address')}</label>
                <div className="relative" ref={suggestionsRef}>
                  {selectedAddress ? (
                    <div className="flex items-center gap-3 rounded-lg border-2 border-emerald-300 bg-emerald-50/50 px-4 py-2.5">
                      <svg className="w-5 h-5 text-emerald-500 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" d="M15 10.5a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z" />
                        <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 10.5c0 7.142-7.5 11.25-7.5 11.25S4.5 17.642 4.5 10.5a7.5 7.5 0 0 1 15 0Z" />
                      </svg>
                      <span className="text-sm text-slate-800 flex-1 font-medium">{selectedAddress.text}</span>
                      <button onClick={clearAddress} className="p-1.5 text-slate-400 hover:text-rose-500 hover:bg-rose-50 rounded-md transition-all">
                        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" d="M6 18 18 6M6 6l12 12" />
                        </svg>
                      </button>
                    </div>
                  ) : (
                    <>
                      <div className="relative">
                        <svg className="absolute left-4 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" d="m21 21-5.197-5.197m0 0A7.5 7.5 0 1 0 5.196 5.196a7.5 7.5 0 0 0 10.607 10.607Z" />
                        </svg>
                        <input
                          type="text"
                          value={addressQuery}
                          onChange={(e) => { setAddressQuery(e.target.value); setShowSuggestions(true); }}
                          onFocus={() => setShowSuggestions(true)}
                          placeholder={t('signupNew.addressPlaceholder')}
                          className={`${INPUT} pl-11`}
                        />
                        {searchingAddress && (
                          <div className="absolute right-4 top-1/2 -translate-y-1/2 w-4 h-4 border-2 border-teal-200 border-t-teal-600 rounded-full animate-spin" />
                        )}
                      </div>
                      {showSuggestions && addressSuggestions.length > 0 && (
                        <div className="absolute z-10 mt-1 w-full bg-white rounded-lg shadow-xl border border-slate-200 overflow-hidden">
                          {addressSuggestions.map((s, i) => (
                            <button
                              key={i}
                              onClick={() => selectAddress(s)}
                              className="w-full text-left px-4 py-2.5 text-sm text-slate-700 hover:bg-teal-50 hover:text-teal-700 flex items-center gap-3 transition-colors border-b border-slate-50 last:border-b-0"
                            >
                              <svg className="w-4 h-4 text-slate-300 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
                                <path strokeLinecap="round" strokeLinejoin="round" d="M15 10.5a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z" />
                                <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 10.5c0 7.142-7.5 11.25-7.5 11.25S4.5 17.642 4.5 10.5a7.5 7.5 0 0 1 15 0Z" />
                              </svg>
                              {s.text}
                            </button>
                          ))}
                        </div>
                      )}
                    </>
                  )}
                </div>
                {selectedAddress && (
                  <p className="text-xs text-slate-400 mt-1 font-mono">DAR: {selectedAddress.darId}</p>
                )}
              </div>
            )}

            {/* DAR ID mode */}
            {addressMode === 'dar' && (
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">{t('signupNew.darId')}</label>
                <div className="flex gap-3">
                  <input
                    type="text"
                    value={darId}
                    onChange={(e) => setDarId(e.target.value)}
                    placeholder={t('signupNew.darIdPlaceholder')}
                    className={`flex-1 ${INPUT} font-mono`}
                  />
                  <button
                    onClick={() => lookupGsrn(darId)}
                    disabled={!darId || lookingUp}
                    className="px-5 py-2.5 bg-slate-900 text-white text-sm font-bold rounded-lg hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed transition-all shadow-sm"
                  >
                    {lookingUp ? t('signupNew.searching') : t('signupNew.lookUp')}
                  </button>
                </div>
              </div>
            )}

            {/* GSRN mode */}
            {addressMode === 'gsrn' && (
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">{t('signupNew.gsrn')}</label>
                <div className="flex gap-3">
                  <input
                    type="text"
                    value={gsrnInput}
                    onChange={(e) => setGsrnInput(e.target.value)}
                    placeholder={t('signupNew.gsrnPlaceholder')}
                    className={`flex-1 ${INPUT} font-mono`}
                  />
                  <button
                    onClick={() => validateGsrn(gsrnInput)}
                    disabled={!gsrnInput || lookingUp}
                    className="px-5 py-2.5 bg-slate-900 text-white text-sm font-bold rounded-lg hover:bg-slate-800 disabled:opacity-40 disabled:cursor-not-allowed transition-all shadow-sm"
                  >
                    {lookingUp ? t('signupNew.validating') : t('signupNew.validate')}
                  </button>
                </div>
                <p className="text-[11px] text-slate-400 mt-1 font-medium">{t('signupNew.gsrnHelper')}</p>
              </div>
            )}

            {lookingUp && (
              <div className="flex items-center gap-3 text-sm text-slate-500 py-1">
                <div className="w-4 h-4 border-2 border-teal-200 border-t-teal-600 rounded-full animate-spin" />
                {t('signupNew.lookingUp')}
              </div>
            )}

            {meteringPoints && meteringPoints.length === 0 && (
              <div className="border-l-4 border-amber-400 bg-amber-50/50 rounded-r-lg px-5 py-3 text-sm text-amber-800 font-medium">
                {t('signupNew.noMeteringPoints')}
              </div>
            )}

            {/* Metering points list */}
            {meteringPoints && meteringPoints.length > 0 && (
              <div>
                <p className="text-[11px] font-black text-slate-400 uppercase tracking-widest mb-2">
                  {meteringPoints.length === 1
                    ? t('signupNew.meteringPointFound')
                    : t('signupNew.meteringPointsSelect', { count: meteringPoints.length })}
                </p>

                {meteringPoints.every((mp) => mp.has_active_process) && (
                  <div className="border-l-4 border-amber-400 bg-amber-50/50 rounded-r-lg px-5 py-3 text-sm text-amber-800 font-medium mb-2">
                    {t('signupNew.allMeteringPointsBlocked')}
                  </div>
                )}

                <div className="space-y-1.5">
                  {meteringPoints.map((mp) => {
                    const blocked = mp.has_active_process;
                    return (
                      <label
                        key={mp.gsrn}
                        className={`group flex items-center gap-4 rounded-lg px-4 py-3 transition-all duration-200 border-2 ${
                          blocked
                            ? 'border-slate-200 bg-slate-50/50 opacity-60 cursor-not-allowed'
                            : selectedGsrn === mp.gsrn
                            ? 'border-teal-500 bg-teal-50/40 shadow-sm cursor-pointer'
                            : 'border-slate-200 hover:border-slate-300 hover:bg-slate-50/50 cursor-pointer'
                        }`}
                      >
                        <input
                          type="radio"
                          name="gsrn"
                          checked={selectedGsrn === mp.gsrn}
                          onChange={() => !blocked && setSelectedGsrn(mp.gsrn)}
                          disabled={blocked}
                          className="accent-teal-600"
                        />
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-2">
                            <p className="font-mono text-sm font-bold text-slate-900 tracking-wide">{mp.gsrn}</p>
                            {blocked && (
                              <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-[10px] font-bold bg-amber-100 text-amber-700 uppercase tracking-wider border border-amber-200">
                                {t('signupNew.activeProcess')}
                              </span>
                            )}
                          </div>
                          <div className="flex items-center gap-2 mt-0.5">
                            <span className="inline-flex px-2 py-0.5 rounded text-[10px] font-bold bg-slate-100 text-slate-500 uppercase tracking-wider">
                              {mp.type}
                            </span>
                            <span className="text-slate-300 text-xs">·</span>
                            <span className="text-xs text-slate-400 font-medium">
                              {t('signupNew.gridArea')} {mp.grid_area_code}
                            </span>
                          </div>
                        </div>
                        {!blocked && selectedGsrn === mp.gsrn && (
                          <svg className="w-5 h-5 text-teal-500 shrink-0" fill="currentColor" viewBox="0 0 20 20">
                            <path fillRule="evenodd" d="M10 18a8 8 0 1 0 0-16 8 8 0 0 0 0 16Zm3.857-9.809a.75.75 0 0 0-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 1 0-1.06 1.061l2.5 2.5a.75.75 0 0 0 1.137-.089l4-5.5Z" clipRule="evenodd" />
                          </svg>
                        )}
                      </label>
                    );
                  })}
                </div>
              </div>
            )}

            {/* ── PRODUCT ── */}
            <SectionDivider label={t('signupNew.selectProduct')} />

            <div>
              <label className="block text-sm font-semibold text-slate-700 mb-1.5">{t('signupNew.selectProduct')}</label>
              <select
                value={selectedProduct || ''}
                onChange={(e) => setSelectedProduct(e.target.value || null)}
                className={INPUT}
              >
                <option value="">—</option>
                {products.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name} — {p.margin_ore_per_kwh} {t('signupNew.marginLabel')}, {p.subscription_kr_per_month} {t('signupNew.subscriptionLabel')}{p.green_energy ? ` (${t('signupNew.green')})` : ''}
                  </option>
                ))}
              </select>
              {selectedProductObj?.description && (
                <p className="text-xs text-slate-400 mt-1">{selectedProductObj.description}</p>
              )}
            </div>

            {/* ── CUSTOMER ── */}
            <SectionDivider label={t('signupNew.customerDetails')} />

            {/* Row 4a: Contact type | Name | CPR/CVR */}
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">{t('signupNew.contactType')}</label>
                <select value={contactType} onChange={(e) => { setContactType(e.target.value); setCprCvr(''); }} className={INPUT}>
                  <option value="private">{t('signupNew.contactPrivate')}</option>
                  <option value="business">{t('signupNew.contactBusiness')}</option>
                </select>
              </div>
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">{t('signupNew.fullName')}</label>
                <input type="text" value={customerName} onChange={(e) => setCustomerName(e.target.value)} className={INPUT} />
              </div>
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">{cprCvrLabel}</label>
                <input
                  type="text"
                  value={cprCvr}
                  onChange={(e) => {
                    const v = e.target.value.replace(/\D/g, '').slice(0, cprCvrExpectedLength);
                    setCprCvr(v);
                  }}
                  placeholder={contactType === 'private' ? '0101901234' : '12345678'}
                  className={`${INPUT} ${cprCvr.length > 0 && !cprCvrValid ? 'border-rose-400 focus:border-rose-500 focus:ring-rose-500/20' : ''}`}
                />
                <p className="text-[11px] text-slate-400 mt-1 font-medium">
                  {contactType === 'private' ? t('signupNew.cprHelper') : t('signupNew.cvrHelper')}
                </p>
              </div>
            </div>

            {/* Row 4b: Email | Mobile | Phone */}
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">{t('signupNew.email')}</label>
                <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} className={INPUT} />
              </div>
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">{t('signupNew.mobile')}</label>
                <input
                  type="tel"
                  value={mobile}
                  onChange={(e) => setMobile(e.target.value)}
                  placeholder={t('signupNew.mobilePlaceholder')}
                  className={INPUT}
                />
              </div>
              <div>
                <label className="block text-sm font-semibold text-slate-700 mb-1.5">{t('signupNew.phone')}</label>
                <input
                  type="tel"
                  value={phone}
                  onChange={(e) => setPhone(e.target.value)}
                  placeholder={t('signupNew.phonePlaceholder')}
                  className={INPUT}
                />
              </div>
            </div>

            {/* Row 4c: Billing address */}
            <div>
              {selectedAddress && (
                <label className="flex items-center gap-3 mb-3 cursor-pointer group">
                  <input type="checkbox" checked={billingSameAsSupply} onChange={(e) => setBillingSameAsSupply(e.target.checked)}
                    className="w-4 h-4 rounded border-slate-300 text-teal-600 focus:ring-teal-500/20 accent-teal-600" />
                  <span className="text-sm text-slate-600 font-medium group-hover:text-slate-800 transition-colors">{t('signupNew.sameAsSupply')}</span>
                </label>
              )}
              {billingSameAsSupply && selectedAddress ? (
                <div className="flex items-center gap-3 rounded-lg border-2 border-emerald-300 bg-emerald-50/50 px-4 py-2.5">
                  <svg className="w-5 h-5 text-emerald-500 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                    <path strokeLinecap="round" strokeLinejoin="round" d="M15 10.5a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z" />
                    <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 10.5c0 7.142-7.5 11.25-7.5 11.25S4.5 17.642 4.5 10.5a7.5 7.5 0 0 1 15 0Z" />
                  </svg>
                  <span className="text-sm text-slate-800 font-medium">{selectedAddress.text}</span>
                </div>
              ) : (
                <div className="relative" ref={billingSuggestionsRef}>
                  <label className="block text-sm font-semibold text-slate-700 mb-1.5">{t('signupNew.billingAddress')}</label>
                  {selectedBillingAddress ? (
                    <div className="flex items-center gap-3 rounded-lg border-2 border-emerald-300 bg-emerald-50/50 px-4 py-2.5">
                      <svg className="w-5 h-5 text-emerald-500 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" d="M15 10.5a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z" />
                        <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 10.5c0 7.142-7.5 11.25-7.5 11.25S4.5 17.642 4.5 10.5a7.5 7.5 0 0 1 15 0Z" />
                      </svg>
                      <span className="text-sm text-slate-800 flex-1 font-medium">{selectedBillingAddress.text}</span>
                      <button onClick={() => { setSelectedBillingAddress(null); setBillingAddressQuery(''); }}
                        className="p-1.5 text-slate-400 hover:text-rose-500 hover:bg-rose-50 rounded-md transition-all">
                        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" d="M6 18 18 6M6 6l12 12" />
                        </svg>
                      </button>
                    </div>
                  ) : (
                    <>
                      <div className="relative">
                        <svg className="absolute left-4 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" d="m21 21-5.197-5.197m0 0A7.5 7.5 0 1 0 5.196 5.196a7.5 7.5 0 0 0 10.607 10.607Z" />
                        </svg>
                        <input
                          type="text"
                          value={billingAddressQuery}
                          onChange={(e) => { setBillingAddressQuery(e.target.value); setShowBillingSuggestions(true); }}
                          onFocus={() => setShowBillingSuggestions(true)}
                          placeholder={t('signupNew.addressPlaceholder')}
                          className={`${INPUT} pl-11`}
                        />
                        {searchingBillingAddress && (
                          <div className="absolute right-4 top-1/2 -translate-y-1/2 w-4 h-4 border-2 border-teal-200 border-t-teal-600 rounded-full animate-spin" />
                        )}
                      </div>
                      {showBillingSuggestions && billingSuggestions.length > 0 && (
                        <div className="absolute z-10 mt-1 w-full bg-white rounded-lg shadow-xl border border-slate-200 overflow-hidden">
                          {billingSuggestions.map((s, i) => (
                            <button
                              key={i}
                              onClick={() => { setSelectedBillingAddress(s); setBillingAddressQuery(s.text); setShowBillingSuggestions(false); }}
                              className="w-full text-left px-4 py-2.5 text-sm text-slate-700 hover:bg-teal-50 hover:text-teal-700 flex items-center gap-3 transition-colors border-b border-slate-50 last:border-b-0"
                            >
                              <svg className="w-4 h-4 text-slate-300 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
                                <path strokeLinecap="round" strokeLinejoin="round" d="M15 10.5a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z" />
                                <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 10.5c0 7.142-7.5 11.25-7.5 11.25S4.5 17.642 4.5 10.5a7.5 7.5 0 0 1 15 0Z" />
                              </svg>
                              {s.text}
                            </button>
                          ))}
                        </div>
                      )}
                    </>
                  )}
                </div>
              )}
            </div>

            {/* ── Submit ── */}
            <div className="pt-4 border-t border-slate-100">
              <button
                onClick={handleSubmit}
                disabled={!canSubmit}
                className="w-full sm:w-auto sm:float-right inline-flex items-center justify-center gap-2.5 px-8 py-3 bg-teal-600 text-white text-sm font-bold rounded-lg shadow-lg shadow-teal-600/25 hover:bg-teal-700 hover:-translate-y-0.5 hover:shadow-xl disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:translate-y-0 disabled:hover:shadow-lg transition-all duration-200"
              >
                {submitting ? (
                  <>
                    <span className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                    {t('signupNew.creating')}
                  </>
                ) : (
                  <>
                    {correctedFromId ? t('signupNew.createCorrectedSignup') : t('signupNew.createSignup')}
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2.5} stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" d="m4.5 12.75 6 6 9-13.5" />
                    </svg>
                  </>
                )}
              </button>
              <div className="clear-both" />
            </div>

          </div>
        </div>
      </div>
    </div>
  );
}

function SectionDivider({ label }) {
  return (
    <div className="flex items-center gap-3 pt-2">
      <div className="w-8 h-px bg-teal-500" />
      <h3 className="text-[11px] font-black uppercase tracking-widest text-slate-400">{label}</h3>
      <div className="flex-1 h-px bg-slate-100" />
    </div>
  );
}
