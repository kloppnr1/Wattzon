import { useState, useEffect, useRef } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { api } from '../api';
import { useDawaSearch } from '../hooks/useDawaSearch';

const STEPS = [
  { key: 'address', label: 'Address', desc: 'Look up metering point' },
  { key: 'product', label: 'Product', desc: 'Choose energy product' },
  { key: 'customer', label: 'Customer', desc: 'Customer details' },
  { key: 'confirm', label: 'Confirm', desc: 'Review and submit' },
];

export default function SignupNew() {
  const navigate = useNavigate();
  const [step, setStep] = useState(0);
  const [products, setProducts] = useState([]);
  const [error, setError] = useState(null);
  const [submitting, setSubmitting] = useState(false);

  // Step 0: Address
  const [addressMode, setAddressMode] = useState('search'); // 'search' or 'dar'
  const [addressQuery, setAddressQuery] = useState('');
  const [selectedAddress, setSelectedAddress] = useState(null);
  const [showSuggestions, setShowSuggestions] = useState(false);
  const [darId, setDarId] = useState('');
  const [meteringPoints, setMeteringPoints] = useState(null);
  const [selectedGsrn, setSelectedGsrn] = useState(null);
  const [lookingUp, setLookingUp] = useState(false);
  const suggestionsRef = useRef(null);

  const { results: addressSuggestions, loading: searchingAddress } = useDawaSearch(addressQuery, {
    enabled: addressMode === 'search' && !selectedAddress,
  });

  // Close suggestions on outside click
  useEffect(() => {
    function handleClick(e) {
      if (suggestionsRef.current && !suggestionsRef.current.contains(e.target)) {
        setShowSuggestions(false);
      }
    }
    document.addEventListener('mousedown', handleClick);
    return () => document.removeEventListener('mousedown', handleClick);
  }, []);

  function selectAddress(suggestion) {
    setSelectedAddress(suggestion);
    setAddressQuery(suggestion.text);
    setDarId(suggestion.darId);
    setShowSuggestions(false);
    // Auto-lookup GSRN
    lookupGsrn(suggestion.darId);
  }

  function clearAddress() {
    setSelectedAddress(null);
    setAddressQuery('');
    setDarId('');
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
      if (result.length === 1) setSelectedGsrn(result[0].gsrn);
    } catch (e) {
      setError(e.message);
    } finally {
      setLookingUp(false);
    }
  }

  // Step 1: Product
  const [selectedProduct, setSelectedProduct] = useState(null);

  // Step 2: Customer
  const [customerName, setCustomerName] = useState('');
  const [cprCvr, setCprCvr] = useState('');
  const [contactType, setContactType] = useState('private');
  const [email, setEmail] = useState('');
  const [phone, setPhone] = useState('');

  // Step 3: Process
  const [type, setType] = useState('switch');
  const [effectiveDate, setEffectiveDate] = useState('');

  useEffect(() => {
    api.getProducts().then(setProducts).catch(() => {});
  }, []);

  async function handleSubmit() {
    setSubmitting(true);
    setError(null);
    try {
      await api.createSignup({
        dar_id: darId,
        customer_name: customerName,
        cpr_cvr: cprCvr,
        contact_type: contactType,
        email,
        phone,
        product_id: selectedProduct,
        type,
        effective_date: effectiveDate,
      });
      navigate('/signups');
    } catch (e) {
      setError(e.message);
      setSubmitting(false);
    }
  }

  const selectedProductObj = products.find((p) => p.id === selectedProduct);

  return (
    <div className="max-w-2xl mx-auto">
      {/* Breadcrumb */}
      <Link to="/signups" className="inline-flex items-center gap-1 text-xs text-slate-400 hover:text-indigo-500 mb-6 transition-colors">
        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
          <path strokeLinecap="round" strokeLinejoin="round" d="M15.75 19.5 8.25 12l7.5-7.5" />
        </svg>
        Back to signups
      </Link>

      {/* Step indicator */}
      <div className="bg-white rounded-xl shadow-sm ring-1 ring-slate-200 p-5 mb-6">
        <div className="flex items-center">
          {STEPS.map((s, i) => (
            <div key={s.key} className="flex items-center flex-1 last:flex-none">
              <div className="flex items-center gap-2.5">
                <div
                  className={`w-8 h-8 rounded-full flex items-center justify-center text-xs font-semibold transition-all ${
                    i < step
                      ? 'bg-emerald-500 text-white'
                      : i === step
                      ? 'bg-indigo-500 text-white shadow-md shadow-indigo-200'
                      : 'bg-slate-100 text-slate-400'
                  }`}
                >
                  {i < step ? (
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2.5} stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" d="m4.5 12.75 6 6 9-13.5" />
                    </svg>
                  ) : (
                    i + 1
                  )}
                </div>
                <div className="hidden sm:block">
                  <p className={`text-xs font-medium ${i <= step ? 'text-slate-700' : 'text-slate-400'}`}>
                    {s.label}
                  </p>
                  <p className="text-[11px] text-slate-400">{s.desc}</p>
                </div>
              </div>
              {i < STEPS.length - 1 && (
                <div className={`flex-1 h-px mx-4 ${i < step ? 'bg-emerald-300' : 'bg-slate-200'}`} />
              )}
            </div>
          ))}
        </div>
      </div>

      {error && (
        <div className="mb-6 bg-red-50 border border-red-200 rounded-lg px-4 py-3 flex items-start gap-3">
          <svg className="w-5 h-5 text-red-400 shrink-0 mt-0.5" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 9v3.75m9-.75a9 9 0 1 1-18 0 9 9 0 0 1 18 0Zm-9 3.75h.008v.008H12v-.008Z" />
          </svg>
          <p className="text-sm text-red-700">{error}</p>
        </div>
      )}

      {/* Step content card */}
      <div className="bg-white rounded-xl shadow-sm ring-1 ring-slate-200 p-6">

        {/* Step 0: Address */}
        {step === 0 && (
          <div className="space-y-5">
            <div>
              <h3 className="text-base font-semibold text-slate-800 mb-1">Address Lookup</h3>
              <p className="text-sm text-slate-500">Search for an address or enter a DAR ID directly.</p>
            </div>

            {/* Mode toggle */}
            <div className="inline-flex bg-slate-100 rounded-lg p-0.5">
              <button
                onClick={() => { setAddressMode('search'); clearAddress(); }}
                className={`px-3 py-1.5 text-xs font-medium rounded-md transition-all ${
                  addressMode === 'search' ? 'bg-white text-slate-700 shadow-sm' : 'text-slate-500 hover:text-slate-700'
                }`}
              >
                Search address
              </button>
              <button
                onClick={() => { setAddressMode('dar'); clearAddress(); }}
                className={`px-3 py-1.5 text-xs font-medium rounded-md transition-all ${
                  addressMode === 'dar' ? 'bg-white text-slate-700 shadow-sm' : 'text-slate-500 hover:text-slate-700'
                }`}
              >
                DAR ID
              </button>
            </div>

            {/* Address search */}
            {addressMode === 'search' && (
              <div>
                <label className="block text-sm font-medium text-slate-700 mb-1.5">Address</label>
                <div className="relative" ref={suggestionsRef}>
                  {selectedAddress ? (
                    <div className="flex items-center gap-2 rounded-lg border border-emerald-300 bg-emerald-50 px-3 py-2">
                      <svg className="w-4 h-4 text-emerald-500 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" d="M15 10.5a3 3 0 1 1-6 0 3 3 0 0 1 6 0Z" />
                        <path strokeLinecap="round" strokeLinejoin="round" d="M19.5 10.5c0 7.142-7.5 11.25-7.5 11.25S4.5 17.642 4.5 10.5a7.5 7.5 0 0 1 15 0Z" />
                      </svg>
                      <span className="text-sm text-slate-800 flex-1">{selectedAddress.text}</span>
                      <button onClick={clearAddress} className="text-slate-400 hover:text-slate-600">
                        <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" d="M6 18 18 6M6 6l12 12" />
                        </svg>
                      </button>
                    </div>
                  ) : (
                    <>
                      <div className="relative">
                        <svg className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-slate-400" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
                          <path strokeLinecap="round" strokeLinejoin="round" d="m21 21-5.197-5.197m0 0A7.5 7.5 0 1 0 5.196 5.196a7.5 7.5 0 0 0 10.607 10.607Z" />
                        </svg>
                        <input
                          type="text"
                          value={addressQuery}
                          onChange={(e) => { setAddressQuery(e.target.value); setShowSuggestions(true); }}
                          onFocus={() => setShowSuggestions(true)}
                          placeholder="Start typing an address, e.g. Vestergade 10, 8000 Aarhus"
                          className="w-full rounded-lg border border-slate-300 pl-9 pr-3 py-2 text-sm text-slate-800 placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent transition-shadow"
                        />
                        {searchingAddress && (
                          <div className="absolute right-3 top-1/2 -translate-y-1/2 w-4 h-4 border-2 border-slate-200 border-t-indigo-500 rounded-full animate-spin" />
                        )}
                      </div>
                      {/* Suggestions dropdown */}
                      {showSuggestions && addressSuggestions.length > 0 && (
                        <div className="absolute z-10 mt-1 w-full bg-white rounded-lg shadow-lg ring-1 ring-slate-200 overflow-hidden">
                          {addressSuggestions.map((s, i) => (
                            <button
                              key={i}
                              onClick={() => selectAddress(s)}
                              className="w-full text-left px-3 py-2.5 text-sm text-slate-700 hover:bg-indigo-50 hover:text-indigo-700 flex items-center gap-2 transition-colors"
                            >
                              <svg className="w-4 h-4 text-slate-400 shrink-0" fill="none" viewBox="0 0 24 24" strokeWidth={1.5} stroke="currentColor">
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
                  <p className="text-[11px] text-slate-400 mt-1">DAR ID: {selectedAddress.darId}</p>
                )}
              </div>
            )}

            {/* DAR ID direct entry */}
            {addressMode === 'dar' && (
              <div>
                <label className="block text-sm font-medium text-slate-700 mb-1.5">DAR ID</label>
                <div className="flex gap-2">
                  <input
                    type="text"
                    value={darId}
                    onChange={(e) => setDarId(e.target.value)}
                    placeholder="0a3f50a0-75eb-32b8-e044-0003ba298018"
                    className="flex-1 rounded-lg border border-slate-300 px-3 py-2 text-sm text-slate-800 placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent transition-shadow"
                  />
                  <button
                    onClick={() => lookupGsrn(darId)}
                    disabled={!darId || lookingUp}
                    className="px-4 py-2 bg-slate-800 text-white text-sm font-medium rounded-lg hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
                  >
                    {lookingUp ? 'Searching...' : 'Look up'}
                  </button>
                </div>
              </div>
            )}

            {/* Loading GSRN */}
            {lookingUp && (
              <div className="flex items-center gap-2 text-sm text-slate-500">
                <div className="w-4 h-4 border-2 border-slate-200 border-t-indigo-500 rounded-full animate-spin" />
                Looking up metering points...
              </div>
            )}

            {/* No results */}
            {meteringPoints && meteringPoints.length === 0 && (
              <div className="bg-amber-50 border border-amber-200 rounded-lg px-4 py-3 text-sm text-amber-700">
                No metering points found for this address.
              </div>
            )}

            {/* Metering point selection */}
            {meteringPoints && meteringPoints.length > 0 && (
              <div>
                <p className="text-sm font-medium text-slate-700 mb-2">
                  {meteringPoints.length === 1
                    ? 'Metering point found:'
                    : `${meteringPoints.length} metering points \u2014 select one:`}
                </p>
                <div className="space-y-2">
                  {meteringPoints.map((mp) => (
                    <label
                      key={mp.gsrn}
                      className={`flex items-center gap-3 border rounded-lg px-4 py-3 cursor-pointer transition-all ${
                        selectedGsrn === mp.gsrn
                          ? 'border-indigo-400 bg-indigo-50 ring-1 ring-indigo-400'
                          : 'border-slate-200 hover:border-slate-300'
                      }`}
                    >
                      <input
                        type="radio"
                        name="gsrn"
                        checked={selectedGsrn === mp.gsrn}
                        onChange={() => setSelectedGsrn(mp.gsrn)}
                        className="accent-indigo-500"
                      />
                      <div>
                        <span className="font-mono text-sm text-slate-800">{mp.gsrn}</span>
                        <span className="ml-3 text-xs text-slate-400">{mp.type} / Grid area {mp.grid_area_code}</span>
                      </div>
                    </label>
                  ))}
                </div>
              </div>
            )}

            <div className="flex justify-end pt-2">
              <button
                onClick={() => { setError(null); setStep(1); }}
                disabled={!selectedGsrn}
                className="px-5 py-2 bg-indigo-500 text-white text-sm font-medium rounded-lg shadow-sm hover:bg-indigo-600 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                Continue
              </button>
            </div>
          </div>
        )}

        {/* Step 1: Product */}
        {step === 1 && (
          <div className="space-y-5">
            <div>
              <h3 className="text-base font-semibold text-slate-800 mb-1">Select Product</h3>
              <p className="text-sm text-slate-500">Choose the energy product for this customer.</p>
            </div>
            <div className="space-y-2">
              {products.map((p) => (
                <label
                  key={p.id}
                  className={`block border rounded-lg px-4 py-3.5 cursor-pointer transition-all ${
                    selectedProduct === p.id
                      ? 'border-indigo-400 bg-indigo-50 ring-1 ring-indigo-400'
                      : 'border-slate-200 hover:border-slate-300'
                  }`}
                >
                  <div className="flex items-start gap-3">
                    <input
                      type="radio"
                      name="product"
                      checked={selectedProduct === p.id}
                      onChange={() => setSelectedProduct(p.id)}
                      className="accent-indigo-500 mt-0.5"
                    />
                    <div className="flex-1">
                      <div className="flex items-center gap-2">
                        <span className="text-sm font-medium text-slate-800">{p.name}</span>
                        {p.green_energy && (
                          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium bg-emerald-50 text-emerald-600 ring-1 ring-inset ring-emerald-200">
                            <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" strokeWidth={2} stroke="currentColor">
                              <path strokeLinecap="round" strokeLinejoin="round" d="M9 12.75 11.25 15 15 9.75M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z" />
                            </svg>
                            Green
                          </span>
                        )}
                      </div>
                      <div className="flex gap-4 mt-1.5 text-xs text-slate-500">
                        <span>{p.energyModel}</span>
                        <span>{p.margin_ore_per_kwh} ore/kWh margin</span>
                        <span>{p.subscription_kr_per_month} kr/mo</span>
                      </div>
                      {p.description && <p className="text-xs text-slate-400 mt-1">{p.description}</p>}
                    </div>
                  </div>
                </label>
              ))}
            </div>
            <div className="flex justify-between pt-2">
              <button onClick={() => setStep(0)} className="px-4 py-2 text-sm text-slate-500 hover:text-slate-700 transition-colors">Back</button>
              <button
                onClick={() => { setError(null); setStep(2); }}
                disabled={!selectedProduct}
                className="px-5 py-2 bg-indigo-500 text-white text-sm font-medium rounded-lg shadow-sm hover:bg-indigo-600 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                Continue
              </button>
            </div>
          </div>
        )}

        {/* Step 2: Customer */}
        {step === 2 && (
          <div className="space-y-5">
            <div>
              <h3 className="text-base font-semibold text-slate-800 mb-1">Customer Details</h3>
              <p className="text-sm text-slate-500">Enter the customer information.</p>
            </div>
            <div className="grid grid-cols-2 gap-4">
              <div className="col-span-2">
                <label className="block text-sm font-medium text-slate-700 mb-1.5">Full name</label>
                <input type="text" value={customerName} onChange={(e) => setCustomerName(e.target.value)}
                  className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm text-slate-800 placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent" />
              </div>
              <div>
                <label className="block text-sm font-medium text-slate-700 mb-1.5">CPR/CVR</label>
                <input type="text" value={cprCvr} onChange={(e) => setCprCvr(e.target.value)} placeholder="0101901234"
                  className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm text-slate-800 placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent" />
              </div>
              <div>
                <label className="block text-sm font-medium text-slate-700 mb-1.5">Contact type</label>
                <select value={contactType} onChange={(e) => setContactType(e.target.value)}
                  className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm text-slate-800 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent">
                  <option value="private">Private</option>
                  <option value="business">Business</option>
                </select>
              </div>
              <div>
                <label className="block text-sm font-medium text-slate-700 mb-1.5">Email</label>
                <input type="email" value={email} onChange={(e) => setEmail(e.target.value)}
                  className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm text-slate-800 placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent" />
              </div>
              <div>
                <label className="block text-sm font-medium text-slate-700 mb-1.5">Phone</label>
                <input type="tel" value={phone} onChange={(e) => setPhone(e.target.value)} placeholder="+4512345678"
                  className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm text-slate-800 placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent" />
              </div>
            </div>
            <div className="flex justify-between pt-2">
              <button onClick={() => setStep(1)} className="px-4 py-2 text-sm text-slate-500 hover:text-slate-700 transition-colors">Back</button>
              <button
                onClick={() => { setError(null); setStep(3); }}
                disabled={!customerName || !cprCvr || !email}
                className="px-5 py-2 bg-indigo-500 text-white text-sm font-medium rounded-lg shadow-sm hover:bg-indigo-600 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                Continue
              </button>
            </div>
          </div>
        )}

        {/* Step 3: Confirm */}
        {step === 3 && (
          <div className="space-y-5">
            <div>
              <h3 className="text-base font-semibold text-slate-800 mb-1">Review & Submit</h3>
              <p className="text-sm text-slate-500">Choose the process type and confirm all details.</p>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-medium text-slate-700 mb-1.5">Process type</label>
                <select value={type} onChange={(e) => setType(e.target.value)}
                  className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm text-slate-800 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent">
                  <option value="switch">Supplier switch (BRS-001)</option>
                  <option value="move_in">Move-in (BRS-009)</option>
                </select>
              </div>
              <div>
                <label className="block text-sm font-medium text-slate-700 mb-1.5">Effective date</label>
                <input type="date" value={effectiveDate} onChange={(e) => setEffectiveDate(e.target.value)}
                  className="w-full rounded-lg border border-slate-300 px-3 py-2 text-sm text-slate-800 focus:outline-none focus:ring-2 focus:ring-indigo-500 focus:border-transparent" />
              </div>
            </div>

            {/* Summary card */}
            <div className="bg-slate-50 border border-slate-200 rounded-lg overflow-hidden">
              <div className="px-4 py-2.5 bg-slate-100 border-b border-slate-200">
                <p className="text-xs font-semibold uppercase tracking-wider text-slate-500">Summary</p>
              </div>
              <div className="divide-y divide-slate-100">
                {selectedAddress && <SummaryRow label="Address" value={selectedAddress.text} />}
                <SummaryRow label="GSRN" value={<span className="font-mono">{selectedGsrn}</span>} />
                <SummaryRow label="Product" value={selectedProductObj?.name} />
                <SummaryRow label="Customer" value={`${customerName} (${cprCvr})`} />
                <SummaryRow label="Contact" value={`${contactType} \u2022 ${email} \u2022 ${phone || '\u2014'}`} />
                <SummaryRow label="Type" value={type === 'move_in' ? 'Move-in (BRS-009)' : 'Supplier switch (BRS-001)'} />
                <SummaryRow label="Effective" value={effectiveDate || 'Not set'} highlight={!effectiveDate} />
              </div>
            </div>

            <div className="flex justify-between pt-2">
              <button onClick={() => setStep(2)} className="px-4 py-2 text-sm text-slate-500 hover:text-slate-700 transition-colors">Back</button>
              <button
                onClick={handleSubmit}
                disabled={!effectiveDate || submitting}
                className="px-6 py-2.5 bg-indigo-500 text-white text-sm font-semibold rounded-lg shadow-sm hover:bg-indigo-600 disabled:opacity-40 disabled:cursor-not-allowed transition-colors"
              >
                {submitting ? (
                  <span className="flex items-center gap-2">
                    <span className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin" />
                    Creating...
                  </span>
                ) : (
                  'Create Signup'
                )}
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

function SummaryRow({ label, value, highlight }) {
  return (
    <div className="flex px-4 py-2.5">
      <span className="w-24 text-xs font-medium text-slate-400 uppercase shrink-0">{label}</span>
      <span className={`text-sm ${highlight ? 'text-amber-600 italic' : 'text-slate-700'}`}>{value}</span>
    </div>
  );
}
