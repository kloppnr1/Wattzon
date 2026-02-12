import React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { LanguageProvider } from '../../i18n/LanguageContext';
import OutstandingOverview from '../OutstandingOverview';
import { api } from '../../api';

vi.mock('../../api', () => ({
  api: {
    getOutstandingCustomers: vi.fn(),
  },
}));

function renderOutstandingOverview() {
  return render(
    <LanguageProvider>
      <MemoryRouter>
        <OutstandingOverview />
      </MemoryRouter>
    </LanguageProvider>
  );
}

function makeCustomer(overrides = {}) {
  return {
    customerId: 'cust-001-full-uuid',
    customerName: 'Acme Corp',
    totalInvoiced: 50000.0,
    totalPaid: 30000.0,
    totalOutstanding: 20000.0,
    totalOverdue: 5000.0,
    invoiceCount: 3,
    ...overrides,
  };
}

describe('OutstandingOverview', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders customer table with outstanding balances', async () => {
    api.getOutstandingCustomers.mockResolvedValue([
      makeCustomer(),
      makeCustomer({ customerId: 'cust-002-full-uuid', customerName: 'Beta Inc', totalOutstanding: 10000, totalOverdue: 2000, invoiceCount: 2 }),
    ]);

    renderOutstandingOverview();

    await waitFor(() => {
      expect(screen.getByText('Acme Corp')).toBeInTheDocument();
    });
    expect(screen.getByText('Beta Inc')).toBeInTheDocument();
  });

  it('shows empty state when no customers', async () => {
    api.getOutstandingCustomers.mockResolvedValue([]);

    renderOutstandingOverview();

    await waitFor(() => {
      expect(screen.getByText('No outstanding balances.')).toBeInTheDocument();
    });
  });

  it('stats cards show customer count, total outstanding, total overdue', async () => {
    api.getOutstandingCustomers.mockResolvedValue([
      makeCustomer({ totalOutstanding: 20000, totalOverdue: 5000 }),
      makeCustomer({ customerId: 'cust-002', totalOutstanding: 10000, totalOverdue: 3000 }),
    ]);

    renderOutstandingOverview();

    await waitFor(() => {
      expect(screen.getByText('2')).toBeInTheDocument();
    });
    expect(screen.getByText('Customers')).toBeInTheDocument();
    expect(screen.getByText('Total Outstanding')).toBeInTheDocument();
    expect(screen.getByText('Total Overdue')).toBeInTheDocument();
  });

  it('customer names link to customer detail pages', async () => {
    api.getOutstandingCustomers.mockResolvedValue([makeCustomer()]);

    renderOutstandingOverview();

    await waitFor(() => {
      expect(screen.getByText('Acme Corp')).toBeInTheDocument();
    });

    const link = screen.getByText('Acme Corp');
    expect(link.closest('a')).toHaveAttribute('href', '/customers/cust-001-full-uuid');
  });

  it('displays error message on API failure', async () => {
    api.getOutstandingCustomers.mockRejectedValue(new Error('Server error'));

    renderOutstandingOverview();

    await waitFor(() => {
      expect(screen.getByText(/Server error/)).toBeInTheDocument();
    });
  });

  it('handles API returning items wrapper', async () => {
    api.getOutstandingCustomers.mockResolvedValue({ items: [makeCustomer()] });

    renderOutstandingOverview();

    await waitFor(() => {
      expect(screen.getByText('Acme Corp')).toBeInTheDocument();
    });
  });
});
