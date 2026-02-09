import { BrowserRouter, Routes, Route } from 'react-router-dom';
import { LanguageProvider } from './i18n/LanguageContext';
import Layout from './layout/Layout';
import Dashboard from './pages/Dashboard';
import SignupList from './pages/SignupList';
import SignupNew from './pages/SignupNew';
import SignupDetail from './pages/SignupDetail';
import CustomerList from './pages/CustomerList';
import CustomerDetail from './pages/CustomerDetail';
import Products from './pages/Products';
import BillingPeriods from './pages/BillingPeriods';
import BillingPeriodDetail from './pages/BillingPeriodDetail';
import SettlementRunDetail from './pages/SettlementRunDetail';
import Messages from './pages/Messages';
import InboundMessageDetail from './pages/InboundMessageDetail';
import OutboundRequestDetail from './pages/OutboundRequestDetail';
import DeadLetterDetail from './pages/DeadLetterDetail';

export default function App() {
  return (
    <LanguageProvider>
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
            <Route path="/billing" element={<BillingPeriods />} />
            <Route path="/billing/periods/:id" element={<BillingPeriodDetail />} />
            <Route path="/billing/runs/:id" element={<SettlementRunDetail />} />
            <Route path="/messages" element={<Messages />} />
            <Route path="/messages/inbound/:id" element={<InboundMessageDetail />} />
            <Route path="/messages/outbound/:id" element={<OutboundRequestDetail />} />
            <Route path="/messages/dead-letters/:id" element={<DeadLetterDetail />} />
          </Route>
        </Routes>
      </BrowserRouter>
    </LanguageProvider>
  );
}
