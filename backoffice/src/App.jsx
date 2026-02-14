import { useState, useEffect } from 'react';
import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { LanguageProvider } from './i18n/LanguageContext';
import Layout from './layout/Layout';
import WattzonIcon from './components/WattzonIcon';
import Dashboard from './pages/Dashboard';
import SignupList from './pages/SignupList';
import SignupNew from './pages/SignupNew';
import SignupDetail from './pages/SignupDetail';
import CustomerList from './pages/CustomerList';
import CustomerDetail from './pages/CustomerDetail';
import Products from './pages/Products';
import Settlement from './pages/Settlement';
import SettlementRunDetail from './pages/SettlementRunDetail';
import SpotPrices from './pages/SpotPrices';
import Messages from './pages/Messages';
import InboundMessageDetail from './pages/InboundMessageDetail';
import OutboundRequestDetail from './pages/OutboundRequestDetail';
import DeadLetterDetail from './pages/DeadLetterDetail';
import CorrectionDetail from './pages/CorrectionDetail';
import Processes from './pages/Processes';
import ProcessDetail from './pages/ProcessDetail';
import InvoiceList from './pages/InvoiceList';
import InvoiceDetail from './pages/InvoiceDetail';
import OutstandingOverview from './pages/OutstandingOverview';
import PaymentList from './pages/PaymentList';
import PaymentDetail from './pages/PaymentDetail';

function SplashScreen({ onDone }) {
  const [exiting, setExiting] = useState(false);

  useEffect(() => {
    const timer = setTimeout(() => {
      setExiting(true);
      setTimeout(onDone, 400);
    }, 1400);
    return () => clearTimeout(timer);
  }, [onDone]);

  return (
    <div
      className={`fixed inset-0 z-[9999] flex items-center justify-center flex-col ${exiting ? 'wz-splash-exit' : ''}`}
      style={{ background: '#1a2a35' }}
    >
      <div
        className="w-20 h-20 rounded-[20px] flex items-center justify-center mb-6 text-white"
        style={{ background: 'rgba(255,255,255,0.08)' }}
      >
        <WattzonIcon size={40} animate />
      </div>
      <p
        className="text-[28px] font-bold text-white tracking-tight mb-4"
        style={{ fontFamily: "'DM Sans', sans-serif", letterSpacing: '-0.5px' }}
      >
        wattzon
      </p>
      <p
        className="text-[10px] uppercase tracking-[2px]"
        style={{ fontFamily: "'Space Mono', monospace", color: 'rgba(255,255,255,0.3)' }}
      >
        Loading settlement data...
      </p>
    </div>
  );
}

export default function App() {
  const [showSplash, setShowSplash] = useState(() => !sessionStorage.getItem('wz_loaded'));

  const handleSplashDone = () => {
    setShowSplash(false);
    sessionStorage.setItem('wz_loaded', '1');
  };

  return (
    <LanguageProvider>
      {showSplash && <SplashScreen onDone={handleSplashDone} />}
      <BrowserRouter>
        <Routes>
          <Route element={<Layout />}>
            <Route path="/" element={<Dashboard />} />
            <Route path="/signups" element={<SignupList />} />
            <Route path="/signups/new" element={<SignupNew />} />
            <Route path="/signups/:id" element={<SignupDetail />} />
            <Route path="/customers" element={<CustomerList />} />
            <Route path="/customers/:id" element={<CustomerDetail />} />
            <Route path="/products" element={<Products />} />
            <Route path="/spot-prices" element={<SpotPrices />} />
            <Route path="/settlement" element={<Settlement />} />
            <Route path="/billing/runs/:id" element={<SettlementRunDetail />} />
            <Route path="/billing/corrections/:batchId" element={<CorrectionDetail />} />
            <Route path="/invoices" element={<InvoiceList />} />
            <Route path="/invoices/:id" element={<InvoiceDetail />} />
            <Route path="/payments" element={<PaymentList />} />
            <Route path="/payments/:id" element={<PaymentDetail />} />
            <Route path="/outstanding" element={<OutstandingOverview />} />
            <Route path="/datahub/messages" element={<Messages />} />
            <Route path="/datahub/messages/inbound/:id" element={<InboundMessageDetail />} />
            <Route path="/datahub/messages/outbound/:id" element={<OutboundRequestDetail />} />
            <Route path="/datahub/messages/dead-letters/:id" element={<DeadLetterDetail />} />
            <Route path="/datahub/processes" element={<Processes />} />
            <Route path="/datahub/processes/:id" element={<ProcessDetail />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </LanguageProvider>
  );
}
