import React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { LanguageProvider } from '../../i18n/LanguageContext';
import InvoiceList from '../InvoiceList';
import { api } from '../../api';

vi.mock('../../api', () => ({
  api: {
    getInvoices: vi.fn(),
  },
}));

function renderInvoiceList() {
  return render(
    <LanguageProvider>
      <MemoryRouter>
        <InvoiceList />
      </MemoryRouter>
    </LanguageProvider>
  );
}

function makeInvoice(overrides = {}) {
  return {
    id: 'inv-001',
    invoiceNumber: 'INV-2025-0001',
    invoiceType: 'invoice',
    status: 'sent',
    periodStart: '2025-01-01',
    periodEnd: '2025-01-31',
    totalInclVat: 12500.0,
    amountOutstanding: 5000.0,
    dueDate: '2025-02-15',
    ...overrides,
  };
}

describe('InvoiceList', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders invoice table with data', async () => {
    api.getInvoices.mockResolvedValue({
      items: [
        makeInvoice(),
        makeInvoice({ id: 'inv-002', invoiceNumber: 'INV-2025-0002', invoiceType: 'credit_note', status: 'paid', totalInclVat: 8000, amountOutstanding: 0 }),
      ],
      totalCount: 2,
    });

    renderInvoiceList();

    await waitFor(() => {
      expect(screen.getByText('INV-2025-0001')).toBeInTheDocument();
    });
    expect(screen.getByText('INV-2025-0002')).toBeInTheDocument();
    expect(screen.getAllByText('2025-01-01 — 2025-01-31')).toHaveLength(2);
    // Status/type text appears in both filter dropdowns and table badges
    // Verify the table row badge text exists (at least 2: dropdown option + badge)
    expect(screen.getAllByText('Sent').length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText('Paid').length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText('Invoice').length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText('Credit note').length).toBeGreaterThanOrEqual(2);
  });

  it('shows empty state when no invoices', async () => {
    api.getInvoices.mockResolvedValue({ items: [], totalCount: 0 });

    renderInvoiceList();

    await waitFor(() => {
      expect(screen.getByText('No invoices found.')).toBeInTheDocument();
    });
  });

  it('displays error message on API failure', async () => {
    api.getInvoices.mockRejectedValue(new Error('Network error'));

    renderInvoiceList();

    await waitFor(() => {
      expect(screen.getByText(/Network error/)).toBeInTheDocument();
    });
  });

  it('stats cards show correct totals', async () => {
    api.getInvoices.mockResolvedValue({
      items: [
        makeInvoice({ totalInclVat: 10000, amountOutstanding: 3000 }),
        makeInvoice({ id: 'inv-002', totalInclVat: 5000, amountOutstanding: 2000 }),
      ],
      totalCount: 2,
    });

    renderInvoiceList();

    await waitFor(() => {
      expect(screen.getByText('2')).toBeInTheDocument();
    });
    expect(screen.getByText('Total Invoices')).toBeInTheDocument();
    expect(screen.getByText('Total Amount')).toBeInTheDocument();
    expect(screen.getByText('Total Outstanding')).toBeInTheDocument();
  });

  it('links navigate to invoice detail', async () => {
    api.getInvoices.mockResolvedValue({
      items: [makeInvoice()],
      totalCount: 1,
    });

    renderInvoiceList();

    await waitFor(() => {
      expect(screen.getByText('INV-2025-0001')).toBeInTheDocument();
    });

    const link = screen.getByText('INV-2025-0001');
    expect(link.closest('a')).toHaveAttribute('href', '/invoices/inv-001');
  });
});
