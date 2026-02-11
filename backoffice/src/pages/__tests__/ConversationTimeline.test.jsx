import React from 'react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { LanguageProvider } from '../../i18n/LanguageContext';
import { ConversationTimeline } from '../Messages';
import { api } from '../../api';

vi.mock('../../api', () => ({
  api: {
    getConversation: vi.fn(),
  },
}));

function renderTimeline(correlationId = 'corr-001') {
  return render(
    <LanguageProvider>
      <MemoryRouter>
        <ConversationTimeline correlationId={correlationId} />
      </MemoryRouter>
    </LanguageProvider>
  );
}

function makeOutbound(overrides = {}) {
  return {
    id: 'out-1',
    processType: 'RSM-001',
    gsrn: '571313100000000001',
    status: 'acknowledged_ok',
    correlationId: 'corr-001',
    sentAt: '2025-06-01T10:00:00Z',
    responseAt: '2025-06-01T10:05:00Z',
    ...overrides,
  };
}

function makeInbound(overrides = {}) {
  return {
    id: 'in-1',
    datahubMessageId: 'DH-001',
    messageType: 'RSM-009',
    correlationId: 'corr-001',
    queueName: 'cim-001',
    status: 'processed',
    receivedAt: '2025-06-01T10:10:00Z',
    processedAt: '2025-06-01T10:10:05Z',
    ...overrides,
  };
}

describe('ConversationTimeline', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // ══════════════════════════════════════════════════════════════
  //  SUNSHINE PATH: RSM-001 → RSM-009 ack → RSM-007 activation
  // ══════════════════════════════════════════════════════════════

  it('renders sunshine path: outbound RSM-001, inbound RSM-009, inbound RSM-007', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-001',
      outbound: [makeOutbound()],
      inbound: [
        makeInbound({ id: 'in-1', messageType: 'RSM-009', receivedAt: '2025-06-01T10:10:00Z' }),
        makeInbound({ id: 'in-2', messageType: 'RSM-007', receivedAt: '2025-06-02T09:00:00Z' }),
      ],
    });

    renderTimeline();

    await waitFor(() => {
      expect(screen.getByText('RSM-001')).toBeInTheDocument();
    });
    expect(screen.getByText('sent to DataHub')).toBeInTheDocument();
    expect(screen.getByText('RSM-009')).toBeInTheDocument();
    expect(screen.getByText('Acknowledgement received')).toBeInTheDocument();
    expect(screen.getByText('RSM-007')).toBeInTheDocument();
    expect(screen.getByText('Activation confirmed')).toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  REJECTION PATH: RSM-001 (error) → RSM-009
  // ══════════════════════════════════════════════════════════════

  it('renders rejection path with error suffix on outbound', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-002',
      outbound: [makeOutbound({ status: 'acknowledged_error' })],
      inbound: [
        makeInbound({ messageType: 'RSM-009' }),
      ],
    });

    renderTimeline('corr-002');

    await waitFor(() => {
      expect(screen.getByText('RSM-001')).toBeInTheDocument();
    });
    expect(screen.getByText('(error)')).toBeInTheDocument();
    expect(screen.getByText('RSM-009')).toBeInTheDocument();
    expect(screen.getByText('Acknowledgement received')).toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  CANCELLATION PATH: RSM-001 → RSM-009 → RSM-003 → RSM-009
  // ══════════════════════════════════════════════════════════════

  it('renders cancellation path: RSM-001 + RSM-003 outbound, two RSM-009 inbound', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-003',
      outbound: [
        makeOutbound({ id: 'out-1', processType: 'RSM-001', sentAt: '2025-06-01T10:00:00Z' }),
        makeOutbound({ id: 'out-2', processType: 'RSM-003', sentAt: '2025-06-02T10:00:00Z' }),
      ],
      inbound: [
        makeInbound({ id: 'in-1', messageType: 'RSM-009', receivedAt: '2025-06-01T10:10:00Z' }),
        makeInbound({ id: 'in-2', messageType: 'RSM-009', receivedAt: '2025-06-02T10:10:00Z' }),
      ],
    });

    renderTimeline('corr-003');

    await waitFor(() => {
      expect(screen.getByText('RSM-001')).toBeInTheDocument();
    });
    expect(screen.getByText('RSM-003')).toBeInTheDocument();
    // Two RSM-009 acknowledgements
    const ackTexts = screen.getAllByText('Acknowledgement received');
    expect(ackTexts).toHaveLength(2);
    // Two "sent to DataHub" labels
    const sentTexts = screen.getAllByText('sent to DataHub');
    expect(sentTexts).toHaveLength(2);
  });

  // ══════════════════════════════════════════════════════════════
  //  CHRONOLOGICAL ORDER: events sorted by timestamp
  // ══════════════════════════════════════════════════════════════

  it('renders events in chronological order regardless of type', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-004',
      outbound: [
        makeOutbound({ id: 'out-1', processType: 'RSM-001', sentAt: '2025-06-01T10:00:00Z' }),
      ],
      inbound: [
        makeInbound({ id: 'in-1', messageType: 'RSM-009', receivedAt: '2025-06-01T10:10:00Z' }),
        makeInbound({ id: 'in-2', messageType: 'RSM-007', receivedAt: '2025-06-02T09:00:00Z' }),
      ],
    });

    renderTimeline('corr-004');

    await waitFor(() => {
      expect(screen.getByText('RSM-001')).toBeInTheDocument();
    });

    // Verify order: RSM-001 before RSM-009 before RSM-007
    const labels = screen.getAllByText(/RSM-00[179]/);
    expect(labels[0].textContent).toBe('RSM-001');
    expect(labels[1].textContent).toBe('RSM-009');
    expect(labels[2].textContent).toBe('RSM-007');
  });

  // ══════════════════════════════════════════════════════════════
  //  RSM-012 and other inbound types use generic "received" text
  // ══════════════════════════════════════════════════════════════

  it('renders generic "received" text for non-RSM-007/009 inbound types', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-005',
      outbound: [makeOutbound()],
      inbound: [
        makeInbound({ id: 'in-1', messageType: 'RSM-009', receivedAt: '2025-06-01T10:10:00Z' }),
        makeInbound({ id: 'in-2', messageType: 'RSM-012', receivedAt: '2025-06-03T02:00:00Z' }),
      ],
    });

    renderTimeline('corr-005');

    await waitFor(() => {
      expect(screen.getByText('RSM-012')).toBeInTheDocument();
    });
    expect(screen.getByText('RSM-012 received')).toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  OUTBOUND ONLY: just RSM-001, no inbound yet
  // ══════════════════════════════════════════════════════════════

  it('renders outbound-only conversation (awaiting DataHub response)', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-006',
      outbound: [makeOutbound()],
      inbound: [],
    });

    renderTimeline('corr-006');

    await waitFor(() => {
      expect(screen.getByText('RSM-001')).toBeInTheDocument();
    });
    expect(screen.getByText('sent to DataHub')).toBeInTheDocument();
    expect(screen.queryByText('Acknowledgement received')).not.toBeInTheDocument();
    expect(screen.queryByText('Activation confirmed')).not.toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  LOADING STATE
  // ══════════════════════════════════════════════════════════════

  it('shows loading text while fetching', () => {
    api.getConversation.mockReturnValue(new Promise(() => {})); // never resolves

    renderTimeline();

    expect(screen.getByText('Loading timeline...')).toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  NO DATA: API returns null / error
  // ══════════════════════════════════════════════════════════════

  it('shows "no messages" when API returns no data', async () => {
    api.getConversation.mockRejectedValue(new Error('Not found'));

    renderTimeline();

    await waitFor(() => {
      expect(screen.getByText('No messages found.')).toBeInTheDocument();
    });
  });

  // ══════════════════════════════════════════════════════════════
  //  END OF SUPPLY: RSM-005 outbound label
  // ══════════════════════════════════════════════════════════════

  it('renders RSM-005 outbound for end-of-supply process', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-007',
      outbound: [makeOutbound({ processType: 'RSM-005' })],
      inbound: [
        makeInbound({ messageType: 'RSM-009' }),
      ],
    });

    renderTimeline('corr-007');

    await waitFor(() => {
      expect(screen.getByText('RSM-005')).toBeInTheDocument();
    });
    expect(screen.getByText('sent to DataHub')).toBeInTheDocument();
  });
});
