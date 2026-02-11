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
    messageType: 'RSM-001',
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
  //  SUNSHINE PATH: RSM-001 → RSM-001 ack → RSM-022 activation
  // ══════════════════════════════════════════════════════════════

  it('renders sunshine path: outbound RSM-001, inbound RSM-001, inbound RSM-022', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-001',
      outbound: [makeOutbound()],
      inbound: [
        makeInbound({ id: 'in-1', messageType: 'RSM-001', receivedAt: '2025-06-01T10:10:00Z' }),
        makeInbound({ id: 'in-2', messageType: 'RSM-022', receivedAt: '2025-06-02T09:00:00Z' }),
      ],
    });

    renderTimeline();

    await waitFor(() => {
      expect(screen.getAllByText('RSM-001')[0]).toBeInTheDocument();
    });
    expect(screen.getByText('sent to DataHub')).toBeInTheDocument();
    expect(screen.getAllByText('RSM-001')[0]).toBeInTheDocument();
    expect(screen.getByText('Acknowledgement received')).toBeInTheDocument();
    expect(screen.getByText('RSM-022')).toBeInTheDocument();
    expect(screen.getByText('Activation confirmed')).toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  REJECTION PATH: RSM-001 (error) → RSM-001
  // ══════════════════════════════════════════════════════════════

  it('renders rejection path with error suffix on outbound', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-002',
      outbound: [makeOutbound({ status: 'acknowledged_error' })],
      inbound: [
        makeInbound({ messageType: 'RSM-001' }),
      ],
    });

    renderTimeline('corr-002');

    await waitFor(() => {
      expect(screen.getAllByText('RSM-001')[0]).toBeInTheDocument();
    });
    expect(screen.getByText('(error)')).toBeInTheDocument();
    expect(screen.getAllByText('RSM-001')[0]).toBeInTheDocument();
    expect(screen.getByText('Acknowledgement received')).toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  CANCELLATION PATH: RSM-001 → RSM-001 → RSM-024 → RSM-001
  // ══════════════════════════════════════════════════════════════

  it('renders cancellation path: RSM-001 + RSM-024 outbound, two RSM-001 inbound', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-003',
      outbound: [
        makeOutbound({ id: 'out-1', processType: 'RSM-001', sentAt: '2025-06-01T10:00:00Z' }),
        makeOutbound({ id: 'out-2', processType: 'RSM-024', sentAt: '2025-06-02T10:00:00Z' }),
      ],
      inbound: [
        makeInbound({ id: 'in-1', messageType: 'RSM-001', receivedAt: '2025-06-01T10:10:00Z' }),
        makeInbound({ id: 'in-2', messageType: 'RSM-001', receivedAt: '2025-06-02T10:10:00Z' }),
      ],
    });

    renderTimeline('corr-003');

    await waitFor(() => {
      expect(screen.getAllByText('RSM-001')[0]).toBeInTheDocument();
    });
    expect(screen.getByText('RSM-024')).toBeInTheDocument();
    // Two RSM-001 acknowledgements
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
        makeInbound({ id: 'in-1', messageType: 'RSM-001', receivedAt: '2025-06-01T10:10:00Z' }),
        makeInbound({ id: 'in-2', messageType: 'RSM-022', receivedAt: '2025-06-02T09:00:00Z' }),
      ],
    });

    renderTimeline('corr-004');

    await waitFor(() => {
      expect(screen.getAllByText('RSM-001')[0]).toBeInTheDocument();
    });

    // Verify order: RSM-001 before RSM-001 before RSM-022
    const labels = screen.getAllByText(/RSM-001|RSM-022/);
    expect(labels[0].textContent).toBe('RSM-001');
    expect(labels[1].textContent).toBe('RSM-001');
    expect(labels[2].textContent).toBe('RSM-022');
  });

  // ══════════════════════════════════════════════════════════════
  //  RSM-012 and other inbound types use generic "received" text
  // ══════════════════════════════════════════════════════════════

  it('renders generic "received" text for non-RSM-022/001 inbound types', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-005',
      outbound: [makeOutbound()],
      inbound: [
        makeInbound({ id: 'in-1', messageType: 'RSM-001', receivedAt: '2025-06-01T10:10:00Z' }),
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
      expect(screen.getAllByText('RSM-001')[0]).toBeInTheDocument();
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
        makeInbound({ messageType: 'RSM-001' }),
      ],
    });

    renderTimeline('corr-007');

    await waitFor(() => {
      expect(screen.getByText('RSM-005')).toBeInTheDocument();
    });
    expect(screen.getByText('sent to DataHub')).toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  EMPTY CONVERSATION: detail exists but no events
  // ══════════════════════════════════════════════════════════════

  it('renders timeline header with no events when conversation has empty arrays', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-empty',
      outbound: [],
      inbound: [],
    });

    renderTimeline('corr-empty');

    await waitFor(() => {
      expect(screen.getByText('Conversation Timeline')).toBeInTheDocument();
    });
    expect(screen.queryByText('sent to DataHub')).not.toBeInTheDocument();
    expect(screen.queryByText('Acknowledgement received')).not.toBeInTheDocument();
    expect(screen.queryByText('Activation confirmed')).not.toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  INBOUND ONLY: no outbound messages (e.g. unsolicited RSM-022)
  // ══════════════════════════════════════════════════════════════

  it('renders inbound-only conversation without outbound events', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-inbound-only',
      outbound: [],
      inbound: [
        makeInbound({ id: 'in-1', messageType: 'RSM-022', receivedAt: '2025-06-02T09:00:00Z' }),
      ],
    });

    renderTimeline('corr-inbound-only');

    await waitFor(() => {
      expect(screen.getByText('RSM-022')).toBeInTheDocument();
    });
    expect(screen.getByText('Activation confirmed')).toBeInTheDocument();
    expect(screen.queryByText('sent to DataHub')).not.toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  LINKS: outbound and inbound link to correct detail routes
  // ══════════════════════════════════════════════════════════════

  it('links outbound to /messages/outbound/{id} and inbound to /messages/inbound/{id}', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-links',
      outbound: [makeOutbound({ id: 'out-abc' })],
      inbound: [
        makeInbound({ id: 'in-xyz', messageType: 'RSM-001' }),
      ],
    });

    renderTimeline('corr-links');

    await waitFor(() => {
      expect(screen.getAllByText('RSM-001')[0]).toBeInTheDocument();
    });

    const rsm001Links = screen.getAllByText('RSM-001').map(el => el.closest('a'));
    const outboundLink = rsm001Links.find(a => a?.getAttribute('href')?.includes('/outbound/'));
    expect(outboundLink).toHaveAttribute('href', '/datahub/messages/outbound/out-abc');

    const inboundLink = rsm001Links.find(a => a?.getAttribute('href')?.includes('/inbound/'));
    expect(inboundLink).toHaveAttribute('href', '/datahub/messages/inbound/in-xyz');
  });

  // ══════════════════════════════════════════════════════════════
  //  NO ERROR SUFFIX for non-error statuses
  // ══════════════════════════════════════════════════════════════

  it('does not show error suffix for acknowledged_ok or pending status', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-ok',
      outbound: [
        makeOutbound({ id: 'out-1', status: 'acknowledged_ok', sentAt: '2025-06-01T10:00:00Z' }),
        makeOutbound({ id: 'out-2', status: 'pending', processType: 'RSM-024', sentAt: '2025-06-02T10:00:00Z' }),
      ],
      inbound: [],
    });

    renderTimeline('corr-ok');

    await waitFor(() => {
      expect(screen.getAllByText('RSM-001')[0]).toBeInTheDocument();
    });
    expect(screen.queryByText('(error)')).not.toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  RSM-004: grid change inbound uses generic "received" label
  // ══════════════════════════════════════════════════════════════

  it('renders RSM-004 inbound with generic received text', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-004-inbound',
      outbound: [],
      inbound: [
        makeInbound({ id: 'in-1', messageType: 'RSM-004', receivedAt: '2025-06-05T14:00:00Z' }),
      ],
    });

    renderTimeline('corr-004-inbound');

    await waitFor(() => {
      expect(screen.getByText('RSM-004')).toBeInTheDocument();
    });
    expect(screen.getByText('RSM-004 received')).toBeInTheDocument();
    expect(screen.queryByText('Acknowledgement received')).not.toBeInTheDocument();
    expect(screen.queryByText('Activation confirmed')).not.toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  RSM-014: aggregation inbound uses generic "received" label
  // ══════════════════════════════════════════════════════════════

  it('renders RSM-014 inbound with generic received text', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-014',
      outbound: [],
      inbound: [
        makeInbound({ id: 'in-1', messageType: 'RSM-014', receivedAt: '2025-06-10T08:00:00Z' }),
      ],
    });

    renderTimeline('corr-014');

    await waitFor(() => {
      expect(screen.getByText('RSM-014')).toBeInTheDocument();
    });
    expect(screen.getByText('RSM-014 received')).toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  FULL CANCELLATION FLOW: RSM-001 → RSM-001 → RSM-024 → RSM-001 → RSM-022 skipped
  // ══════════════════════════════════════════════════════════════

  it('renders full cancellation flow with interleaved outbound and inbound', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-full-cancel',
      outbound: [
        makeOutbound({ id: 'out-1', processType: 'RSM-001', sentAt: '2025-06-01T10:00:00Z' }),
        makeOutbound({ id: 'out-2', processType: 'RSM-024', sentAt: '2025-06-01T11:00:00Z' }),
      ],
      inbound: [
        makeInbound({ id: 'in-1', messageType: 'RSM-001', receivedAt: '2025-06-01T10:30:00Z' }),
        makeInbound({ id: 'in-2', messageType: 'RSM-001', receivedAt: '2025-06-01T11:30:00Z' }),
      ],
    });

    renderTimeline('corr-full-cancel');

    await waitFor(() => {
      expect(screen.getAllByText('RSM-001')[0]).toBeInTheDocument();
    });

    // Verify chronological order: RSM-001 → RSM-001 → RSM-024 → RSM-001
    const allLabels = screen.getAllByText(/RSM-001|RSM-024/);
    expect(allLabels).toHaveLength(4);
    expect(allLabels[0].textContent).toBe('RSM-001');
    expect(allLabels[1].textContent).toBe('RSM-001');
    expect(allLabels[2].textContent).toBe('RSM-024');
    expect(allLabels[3].textContent).toBe('RSM-001');
  });

  // ══════════════════════════════════════════════════════════════
  //  OUT-OF-ORDER TIMESTAMPS: inbound arrives before outbound
  // ══════════════════════════════════════════════════════════════

  it('sorts events by timestamp even when inbound precedes outbound', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-order',
      outbound: [
        makeOutbound({ id: 'out-1', processType: 'RSM-001', sentAt: '2025-06-01T12:00:00Z' }),
      ],
      inbound: [
        makeInbound({ id: 'in-1', messageType: 'RSM-022', receivedAt: '2025-06-01T08:00:00Z' }),
      ],
    });

    renderTimeline('corr-order');

    await waitFor(() => {
      expect(screen.getAllByText('RSM-001')[0]).toBeInTheDocument();
    });

    // RSM-022 (08:00) should appear before RSM-001 (12:00)
    const labels = screen.getAllByText(/RSM-001|RSM-022/);
    expect(labels[0].textContent).toBe('RSM-022');
    expect(labels[1].textContent).toBe('RSM-001');
  });

  // ══════════════════════════════════════════════════════════════
  //  MULTIPLE RSM-022: duplicate activations render correctly
  // ══════════════════════════════════════════════════════════════

  it('renders multiple RSM-022 activation messages', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-dup-007',
      outbound: [makeOutbound()],
      inbound: [
        makeInbound({ id: 'in-1', messageType: 'RSM-001', receivedAt: '2025-06-01T10:10:00Z' }),
        makeInbound({ id: 'in-2', messageType: 'RSM-022', receivedAt: '2025-06-02T09:00:00Z' }),
        makeInbound({ id: 'in-3', messageType: 'RSM-022', receivedAt: '2025-06-02T09:05:00Z' }),
      ],
    });

    renderTimeline('corr-dup-007');

    await waitFor(() => {
      expect(screen.getAllByText('RSM-001')[0]).toBeInTheDocument();
    });

    const activations = screen.getAllByText('Activation confirmed');
    expect(activations).toHaveLength(2);
  });

  // ══════════════════════════════════════════════════════════════
  //  NULL TIMESTAMP: time shows '-' when sentAt/receivedAt is null
  // ══════════════════════════════════════════════════════════════

  it('shows dash for null timestamps', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-null-time',
      outbound: [makeOutbound({ sentAt: null })],
      inbound: [],
    });

    renderTimeline('corr-null-time');

    await waitFor(() => {
      expect(screen.getAllByText('RSM-001')[0]).toBeInTheDocument();
    });
    expect(screen.getByText('-')).toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  API RESOLVES NULL: detail is null → "No messages found."
  // ══════════════════════════════════════════════════════════════

  it('shows "no messages" when API resolves with null', async () => {
    api.getConversation.mockResolvedValue(null);

    renderTimeline('corr-null');

    await waitFor(() => {
      expect(screen.getByText('No messages found.')).toBeInTheDocument();
    });
  });

  // ══════════════════════════════════════════════════════════════
  //  API CALLED WITH CORRECT CORRELATION ID
  // ══════════════════════════════════════════════════════════════

  it('calls api.getConversation with the provided correlationId', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'my-specific-corr',
      outbound: [],
      inbound: [],
    });

    renderTimeline('my-specific-corr');

    await waitFor(() => {
      expect(api.getConversation).toHaveBeenCalledWith('my-specific-corr');
    });
    expect(api.getConversation).toHaveBeenCalledTimes(1);
  });

  // ══════════════════════════════════════════════════════════════
  //  MANY EVENTS: large conversation renders all events
  // ══════════════════════════════════════════════════════════════

  it('renders all events in a large conversation', async () => {
    const outbound = [
      makeOutbound({ id: 'out-1', processType: 'RSM-001', sentAt: '2025-06-01T10:00:00Z' }),
      makeOutbound({ id: 'out-2', processType: 'RSM-024', sentAt: '2025-06-03T10:00:00Z' }),
      makeOutbound({ id: 'out-3', processType: 'RSM-005', sentAt: '2025-06-05T10:00:00Z' }),
    ];
    const inbound = [
      makeInbound({ id: 'in-1', messageType: 'RSM-001', receivedAt: '2025-06-01T10:10:00Z' }),
      makeInbound({ id: 'in-2', messageType: 'RSM-022', receivedAt: '2025-06-02T09:00:00Z' }),
      makeInbound({ id: 'in-3', messageType: 'RSM-001', receivedAt: '2025-06-03T10:10:00Z' }),
      makeInbound({ id: 'in-4', messageType: 'RSM-012', receivedAt: '2025-06-04T02:00:00Z' }),
      makeInbound({ id: 'in-5', messageType: 'RSM-001', receivedAt: '2025-06-05T10:10:00Z' }),
    ];

    api.getConversation.mockResolvedValue({
      correlationId: 'corr-large',
      outbound,
      inbound,
    });

    renderTimeline('corr-large');

    await waitFor(() => {
      expect(screen.getAllByText('RSM-001').length).toBeGreaterThan(0);
    });

    // 3 outbound "sent to DataHub" + various inbound labels
    const sentTexts = screen.getAllByText('sent to DataHub');
    expect(sentTexts).toHaveLength(3);

    const ackTexts = screen.getAllByText('Acknowledgement received');
    expect(ackTexts).toHaveLength(3);

    expect(screen.getByText('Activation confirmed')).toBeInTheDocument();
    expect(screen.getByText('RSM-012 received')).toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  MIXED INBOUND TYPES: RSM-022, RSM-001, RSM-012, RSM-004
  //  all render with correct labels and colors
  // ══════════════════════════════════════════════════════════════

  it('renders mixed inbound types each with correct label', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-mixed',
      outbound: [],
      inbound: [
        makeInbound({ id: 'in-1', messageType: 'RSM-001', receivedAt: '2025-06-01T10:00:00Z' }),
        makeInbound({ id: 'in-2', messageType: 'RSM-022', receivedAt: '2025-06-02T10:00:00Z' }),
        makeInbound({ id: 'in-3', messageType: 'RSM-012', receivedAt: '2025-06-03T10:00:00Z' }),
        makeInbound({ id: 'in-4', messageType: 'RSM-004', receivedAt: '2025-06-04T10:00:00Z' }),
      ],
    });

    renderTimeline('corr-mixed');

    await waitFor(() => {
      expect(screen.getAllByText('RSM-001')[0]).toBeInTheDocument();
    });

    expect(screen.getByText('Acknowledgement received')).toBeInTheDocument();
    expect(screen.getByText('Activation confirmed')).toBeInTheDocument();
    expect(screen.getByText('RSM-012 received')).toBeInTheDocument();
    expect(screen.getByText('RSM-004 received')).toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  MULTIPLE OUTBOUND ERROR: two outbound with error status
  // ══════════════════════════════════════════════════════════════

  it('renders error suffix on each outbound with acknowledged_error status', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-multi-err',
      outbound: [
        makeOutbound({ id: 'out-1', processType: 'RSM-001', status: 'acknowledged_error', sentAt: '2025-06-01T10:00:00Z' }),
        makeOutbound({ id: 'out-2', processType: 'RSM-024', status: 'acknowledged_error', sentAt: '2025-06-02T10:00:00Z' }),
      ],
      inbound: [],
    });

    renderTimeline('corr-multi-err');

    await waitFor(() => {
      expect(screen.getAllByText('RSM-001')[0]).toBeInTheDocument();
    });

    const errorSuffixes = screen.getAllByText('(error)');
    expect(errorSuffixes).toHaveLength(2);
  });

  // ══════════════════════════════════════════════════════════════
  //  SAME TIMESTAMP: outbound and inbound at identical time
  // ══════════════════════════════════════════════════════════════

  it('handles events with identical timestamps without crashing', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-same-time',
      outbound: [makeOutbound({ sentAt: '2025-06-01T10:00:00Z' })],
      inbound: [
        makeInbound({ messageType: 'RSM-001', receivedAt: '2025-06-01T10:00:00Z' }),
      ],
    });

    renderTimeline('corr-same-time');

    await waitFor(() => {
      expect(screen.getAllByText('RSM-001').length).toBeGreaterThan(0);
    });
    expect(screen.getAllByText('RSM-001')).toHaveLength(2);
    expect(screen.getByText('sent to DataHub')).toBeInTheDocument();
    expect(screen.getByText('Acknowledgement received')).toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  CANCELLATION BEFORE ACK: RSM-001 → RSM-024 (no RSM-001 yet)
  // ══════════════════════════════════════════════════════════════

  it('renders cancellation sent before acknowledgement arrives', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-cancel-early',
      outbound: [
        makeOutbound({ id: 'out-1', processType: 'RSM-001', sentAt: '2025-06-01T10:00:00Z' }),
        makeOutbound({ id: 'out-2', processType: 'RSM-024', sentAt: '2025-06-01T10:02:00Z' }),
      ],
      inbound: [],
    });

    renderTimeline('corr-cancel-early');

    await waitFor(() => {
      expect(screen.getAllByText('RSM-001')[0]).toBeInTheDocument();
    });
    expect(screen.getByText('RSM-024')).toBeInTheDocument();
    const sentTexts = screen.getAllByText('sent to DataHub');
    expect(sentTexts).toHaveLength(2);
    expect(screen.queryByText('Acknowledgement received')).not.toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  TIMELINE HEADER: always renders "Conversation Timeline"
  // ══════════════════════════════════════════════════════════════

  it('always renders the timeline section header', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-header',
      outbound: [makeOutbound()],
      inbound: [],
    });

    renderTimeline('corr-header');

    await waitFor(() => {
      expect(screen.getByText('Conversation Timeline')).toBeInTheDocument();
    });
  });

  // ══════════════════════════════════════════════════════════════
  //  RSM-028: Customer data received
  // ══════════════════════════════════════════════════════════════

  it('renders RSM-028 inbound with "Customer data received" label', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-028',
      outbound: [],
      inbound: [
        makeInbound({ id: 'in-1', messageType: 'RSM-028', receivedAt: '2025-06-01T10:15:00Z' }),
      ],
    });

    renderTimeline('corr-028');

    await waitFor(() => {
      expect(screen.getByText('RSM-028')).toBeInTheDocument();
    });
    expect(screen.getByText('Customer data received')).toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  RSM-031: Price attachments received
  // ══════════════════════════════════════════════════════════════

  it('renders RSM-031 inbound with "Price attachments received" label', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-031',
      outbound: [],
      inbound: [
        makeInbound({ id: 'in-1', messageType: 'RSM-031', receivedAt: '2025-06-01T10:16:00Z' }),
      ],
    });

    renderTimeline('corr-031');

    await waitFor(() => {
      expect(screen.getByText('RSM-031')).toBeInTheDocument();
    });
    expect(screen.getByText('Price attachments received')).toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  FULL BRS-001 FLOW: RSM-001 → RSM-028 → RSM-031 → RSM-022
  // ══════════════════════════════════════════════════════════════

  it('renders full BRS-001 flow with RSM-028 and RSM-031', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-full-brs001',
      outbound: [
        makeOutbound({ id: 'out-1', processType: 'RSM-001', sentAt: '2025-06-01T10:00:00Z' }),
      ],
      inbound: [
        makeInbound({ id: 'in-1', messageType: 'RSM-001', receivedAt: '2025-06-01T10:10:00Z' }),
        makeInbound({ id: 'in-2', messageType: 'RSM-028', receivedAt: '2025-06-01T10:15:00Z' }),
        makeInbound({ id: 'in-3', messageType: 'RSM-031', receivedAt: '2025-06-01T10:16:00Z' }),
        makeInbound({ id: 'in-4', messageType: 'RSM-022', receivedAt: '2025-06-02T09:00:00Z' }),
      ],
    });

    renderTimeline('corr-full-brs001');

    await waitFor(() => {
      expect(screen.getAllByText('RSM-001')[0]).toBeInTheDocument();
    });

    expect(screen.getByText('Acknowledgement received')).toBeInTheDocument();
    expect(screen.getByText('Customer data received')).toBeInTheDocument();
    expect(screen.getByText('Price attachments received')).toBeInTheDocument();
    expect(screen.getByText('Activation confirmed')).toBeInTheDocument();
  });

  // ══════════════════════════════════════════════════════════════
  //  OUTBOUND ICON: outbound events show ">" icon
  //  INBOUND ICON: inbound events show "<" icon
  // ══════════════════════════════════════════════════════════════

  it('renders ">" for outbound and "<" for inbound events', async () => {
    api.getConversation.mockResolvedValue({
      correlationId: 'corr-icons',
      outbound: [makeOutbound()],
      inbound: [
        makeInbound({ messageType: 'RSM-001' }),
      ],
    });

    renderTimeline('corr-icons');

    await waitFor(() => {
      expect(screen.getAllByText('RSM-001').length).toBeGreaterThan(0);
    });

    expect(screen.getByText('>')).toBeInTheDocument();
    expect(screen.getByText('<')).toBeInTheDocument();
  });
});
