namespace DataHub.Settlement.Infrastructure.Dashboard;

public sealed class ChangeOfSupplierContext
{
    public ProcessTimeline Timeline { get; }
    public DateOnly EffectiveDate { get; }
    public Guid ProcessRequestId { get; set; }
    public string Gsrn { get; }
    public string CustomerName { get; }

    public bool IsSeeded { get; set; }
    public bool IsBrsSubmitted { get; set; }
    public bool IsAcknowledged { get; set; }
    public bool IsRsm022Received { get; set; }
    public bool IsEffectuated { get; set; }
    public bool IsMeteringReceived { get; set; }
    public bool IsSettled { get; set; }

    /// <summary>Number of daily RSM-012 deliveries received so far.</summary>
    public int MeteringDaysDelivered { get; set; }
    /// <summary>Total days of metering data expected for the period.</summary>
    public int TotalMeteringDays { get; }

    public ChangeOfSupplierContext(string gsrn, string customerName, DateOnly effectiveDate)
    {
        Gsrn = gsrn;
        CustomerName = customerName;
        EffectiveDate = effectiveDate;
        Timeline = DataHubTimeline.BuildChangeOfSupplierTimeline(effectiveDate);
        TotalMeteringDays = effectiveDate.AddMonths(1).DayNumber - effectiveDate.DayNumber;
    }
}

public sealed class MoveInContext
{
    public ProcessTimeline Timeline { get; }
    public DateOnly EffectiveDate { get; }
    public Guid ProcessRequestId { get; set; }
    public string Gsrn { get; }
    public string CustomerName { get; }

    public bool IsSeeded { get; set; }
    public bool IsBrsSubmitted { get; set; }
    public bool IsAcknowledged { get; set; }
    public bool IsRsm022Received { get; set; }
    public bool IsEffectuated { get; set; }
    public bool IsMeteringReceived { get; set; }
    public bool IsSettled { get; set; }

    /// <summary>Number of daily RSM-012 deliveries received so far.</summary>
    public int MeteringDaysDelivered { get; set; }
    /// <summary>Total days of metering data expected for the period.</summary>
    public int TotalMeteringDays { get; }

    public MoveInContext(string gsrn, string customerName, DateOnly effectiveDate)
    {
        Gsrn = gsrn;
        CustomerName = customerName;
        EffectiveDate = effectiveDate;
        Timeline = DataHubTimeline.BuildMoveInTimeline(effectiveDate);
        TotalMeteringDays = effectiveDate.AddMonths(1).DayNumber - effectiveDate.DayNumber;
    }
}

public sealed class AcontoChangeOfSupplierContext
{
    public ProcessTimeline Timeline { get; }
    public DateOnly EffectiveDate { get; }
    public Guid ProcessRequestId { get; set; }
    public string Gsrn { get; }
    public string CustomerName { get; }

    public bool IsSeeded { get; set; }
    public bool IsBrsSubmitted { get; set; }
    public bool IsAcknowledged { get; set; }
    public bool IsAcontoEstimated { get; set; }
    public bool IsInvoiceSent { get; set; }
    public bool IsRsm022Received { get; set; }
    public bool IsEffectuated { get; set; }
    public bool IsAcontoPaid { get; set; }
    public bool IsAcontoSettled { get; set; }
    public bool IsMeteringReceived { get; set; }
    public decimal AcontoEstimate { get; set; }

    /// <summary>Number of daily RSM-012 deliveries received so far.</summary>
    public int MeteringDaysDelivered { get; set; }
    /// <summary>Total days of metering data expected for the period.</summary>
    public int TotalMeteringDays { get; }

    public AcontoChangeOfSupplierContext(string gsrn, string customerName, DateOnly effectiveDate)
    {
        Gsrn = gsrn;
        CustomerName = customerName;
        EffectiveDate = effectiveDate;
        Timeline = DataHubTimeline.BuildAcontoChangeOfSupplierTimeline(effectiveDate);
        TotalMeteringDays = effectiveDate.AddMonths(1).DayNumber - effectiveDate.DayNumber;
    }
}
