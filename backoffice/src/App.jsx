import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import Layout from './layout/Layout';
import SignupList from './pages/SignupList';
import SignupNew from './pages/SignupNew';
import SignupDetail from './pages/SignupDetail';
import CustomerList from './pages/CustomerList';
import CustomerDetail from './pages/CustomerDetail';
import Products from './pages/Products';

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route path="/" element={<Navigate to="/signups" replace />} />
          <Route path="/signups" element={<SignupList />} />
          <Route path="/signups/new" element={<SignupNew />} />
          <Route path="/signups/:id" element={<SignupDetail />} />
          <Route path="/customers" element={<CustomerList />} />
          <Route path="/customers/:id" element={<CustomerDetail />} />
          <Route path="/products" element={<Products />} />
        </Route>
      </Routes>
    </BrowserRouter>
  );
}
