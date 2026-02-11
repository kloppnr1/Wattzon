using System.Text.Json;
using DataHub.Settlement.Application.DataHub;

namespace DataHub.Settlement.Simulator;

public static class ScenarioLoader
{
    public static void Load(SimulatorState state, string scenarioName)
    {
        state.Reset();

        switch (scenarioName)
        {
            case "sunshine":
                LoadSunshine(state);
                break;
            case "rejection":
                LoadRejection(state);
                break;
            case "cancellation":
                LoadCancellation(state);
                break;
            case "full_lifecycle":
                LoadFullLifecycle(state);
                break;
            case "move_in":
                LoadMoveIn(state);
                break;
            case "move_out":
                LoadMoveOut(state);
                break;
            case "auto_cancel":
                LoadAutoCancel(state);
                break;
            case "forced_switch":
                LoadForcedTransfer(state);
                break;
            default:
                throw new ArgumentException($"Unknown scenario: {scenarioName}");
        }
    }

    private static void LoadSunshine(SimulatorState state)
    {
        // RSM-001: Acceptance
        state.EnqueueMessage("MasterData", "RSM-001", "corr-sim-001",
            BuildRsm001AcceptJson("corr-sim-001"));

        // RSM-028: Customer data
        state.EnqueueMessage("MasterData", "RSM-028", "corr-sim-001",
            BuildRsm028Json("571313100000012345", "Anders Hansen", "1234567890"));

        // RSM-031: Price attachments
        state.EnqueueMessage("MasterData", "RSM-031", "corr-sim-001",
            BuildRsm031Json("571313100000012345", "2025-01-01T00:00:00Z"));

        // RSM-022: Master data confirmation
        state.EnqueueMessage("MasterData", "RSM-022", "corr-sim-001", BuildRsm022Json());

        // RSM-012: Metering data for January (744 hours)
        state.EnqueueMessage("Timeseries", "RSM-012", null, BuildRsm012Json(
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero),
            744));
    }

    private static void LoadRejection(SimulatorState state)
    {
        // BRS-001 rejection receipt
        state.EnqueueMessage("MasterData", "RSM-022-REJECT", "corr-sim-reject", BuildRejectionJson("E16", "Invalid GSRN checksum"));
    }

    private static void LoadCancellation(SimulatorState state)
    {
        // RSM-022: confirmation
        state.EnqueueMessage("MasterData", "RSM-022", "corr-sim-cancel", BuildRsm022Json());
    }

    private static void LoadAutoCancel(SimulatorState state)
    {
        // RSM-004/D11: Auto-cancellation due to customer data deadline
        state.EnqueueMessage("MasterData", "RSM-004", "corr-sim-autocancel",
            BuildRsm004D11Json("571313100000012345", "2025-02-01T00:00:00Z"));
    }

    private static void LoadFullLifecycle(SimulatorState state)
    {
        // Phase 0: Customer data and price attachments
        state.EnqueueMessage("MasterData", "RSM-028", "corr-sim-lifecycle",
            BuildRsm028Json("571313100000012345", "Lars Nielsen", "9876543210"));
        state.EnqueueMessage("MasterData", "RSM-031", "corr-sim-lifecycle",
            BuildRsm031Json("571313100000012345", "2025-01-01T00:00:00Z"));

        // Phase 1: Onboarding
        state.EnqueueMessage("MasterData", "RSM-022", "corr-sim-lifecycle", BuildRsm022Json());
        state.EnqueueMessage("Timeseries", "RSM-012", null, BuildRsm012Json(
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero),
            744));

        // Phase 2: RSM-004 grid area change
        state.EnqueueMessage("MasterData", "RSM-004", null, BuildRsm004Json());

        // Phase 3: Final metering data up to switch
        state.EnqueueMessage("Timeseries", "RSM-012", null, BuildRsm012Json(
            new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 2, 16, 0, 0, 0, TimeSpan.Zero),
            360));
    }

    private static void LoadMoveIn(SimulatorState state)
    {
        // RSM-022: Master data confirmation
        state.EnqueueMessage("MasterData", "RSM-022", "corr-sim-movein", BuildRsm022Json());

        // RSM-012: Metering data for January (744 hours)
        state.EnqueueMessage("Timeseries", "RSM-012", null, BuildRsm012Json(
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero),
            744));
    }

    private static void LoadMoveOut(SimulatorState state)
    {
        // Phase 1: RSM-022 confirmation for initial supply
        state.EnqueueMessage("MasterData", "RSM-022", "corr-sim-moveout", BuildRsm022Json());

        // Phase 2: January metering data
        state.EnqueueMessage("Timeseries", "RSM-012", null, BuildRsm012Json(
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero),
            744));

        // Phase 3: Final partial metering data (Feb 1-16)
        state.EnqueueMessage("Timeseries", "RSM-012", null, BuildRsm012Json(
            new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 2, 16, 0, 0, 0, TimeSpan.Zero),
            360));
    }

    private static string BuildRsm022Json()
        => BuildRsm022Json("571313100000012345", "2025-01-01T00:00:00Z");

    internal static string BuildRsm022Json(string gsrn, string effectiveDate)
    {
        var doc = new
        {
            MarketDocument = new
            {
                mRID = $"msg-rsm022-{Guid.NewGuid():N}",
                type = "E44",
                MktActivityRecord = new
                {
                    MarketEvaluationPoint = new
                    {
                        mRID = gsrn,
                        type = "E17",
                        settlementMethod = "D01",
                        linkedMarketEvaluationPoint = new { mRID = "344" },
                        inDomain = new { mRID = "5790000392261" },
                    },
                    Period = new { timeInterval = new { start = effectiveDate } },
                },
            },
        };
        return JsonSerializer.Serialize(doc);
    }

    internal static string BuildRsm012Json(DateTimeOffset start, DateTimeOffset end, int hours)
    {
        var points = new List<object>();
        for (var i = 1; i <= hours; i++)
        {
            var hour = ((i - 1) % 24);
            var kwh = hour switch
            {
                >= 0 and <= 5 => 0.300m,
                >= 6 and <= 15 => 0.500m,
                >= 16 and <= 19 => 1.200m,
                _ => 0.400m,
            };
            points.Add(new { position = i, quantity = kwh, quality = "A01" });
        }

        var registrationTime = end.AddHours(6).ToString("O"); // Typically available ~6h after period ends

        var doc = new
        {
            MarketDocument = new
            {
                mRID = $"msg-rsm012-{Guid.NewGuid():N}",
                type = "E66",
                Process = new { ProcessType = "E23" },
                Sender_MarketParticipant = new
                {
                    mRID = "5790001330552",
                    MarketRole = new { type = "DGL" },
                },
                Receiver_MarketParticipant = new
                {
                    mRID = "5790002000000",
                    MarketRole = new { type = "DDQ" },
                },
                createdDateTime = registrationTime,
                Series = new[]
                {
                    new
                    {
                        mRID = $"txn-{Guid.NewGuid():N}",
                        MarketEvaluationPoint = new { mRID = "571313100000012345", type = "E17" },
                        Product = "8716867000030",
                        Quantity_Measure_Unit = new { name = "KWH" },
                        Registration_DateAndOrTime = new { dateTime = registrationTime },
                        Period = new
                        {
                            resolution = "PT1H",
                            timeInterval = new { start = start.ToString("O"), end = end.ToString("O") },
                            Point = points,
                        },
                    },
                },
            },
        };
        return JsonSerializer.Serialize(doc);
    }

    private static string BuildRejectionJson(string errorCode, string errorMessage)
    {
        var doc = new
        {
            MarketDocument = new
            {
                mRID = $"msg-reject-{Guid.NewGuid():N}",
                type = "E59",
                Reason = new { code = errorCode, text = errorMessage },
            },
        };
        return JsonSerializer.Serialize(doc);
    }

    internal static string BuildRsm028Json(string gsrn, string customerName, string cprCvr, string customerType = "person", bool includeCpr = true)
    {
        object customer = includeCpr
            ? new
            {
                name = customerName,
                mRID = cprCvr,
                type = customerType,
                phone = "+45 12345678",
                email = $"{customerName.ToLower().Replace(" ", ".")}@example.dk",
            }
            : (object)new
            {
                name = customerName,
                type = customerType,
            };

        var doc = new
        {
            MarketDocument = new
            {
                mRID = $"msg-rsm028-{Guid.NewGuid():N}",
                type = "E44",
                MktActivityRecord = new
                {
                    MarketEvaluationPoint = new { mRID = gsrn },
                    Customer = customer,
                },
            },
        };
        return JsonSerializer.Serialize(doc);
    }

    internal static string BuildRsm031Json(string gsrn, string effectiveDate)
    {
        var doc = new
        {
            MarketDocument = new
            {
                mRID = $"msg-rsm031-{Guid.NewGuid():N}",
                type = "E44",
                MktActivityRecord = new
                {
                    MarketEvaluationPoint = new { mRID = gsrn },
                    ChargeType = new[]
                    {
                        new { mRID = "40000", type = "grid", effectiveDate = effectiveDate, terminationDate = (string?)null },
                        new { mRID = "45013", type = "transmission", effectiveDate = effectiveDate, terminationDate = (string?)null },
                        new { mRID = "40010", type = "system", effectiveDate = effectiveDate, terminationDate = (string?)null },
                    },
                },
            },
        };
        return JsonSerializer.Serialize(doc);
    }

    internal static string BuildRsm004D11Json(string gsrn, string effectiveDate)
    {
        var doc = new
        {
            MarketDocument = new
            {
                mRID = $"msg-rsm004-d11-{Guid.NewGuid():N}",
                type = "E44",
                MktActivityRecord = new
                {
                    MarketEvaluationPoint = new
                    {
                        mRID = gsrn,
                    },
                    Reason = new { code = "D11", text = "Customer data deadline exceeded" },
                    Period = new { timeInterval = new { start = effectiveDate } },
                },
            },
        };
        return JsonSerializer.Serialize(doc);
    }

    private static string BuildRsm004Json()
    {
        var doc = new
        {
            MarketDocument = new
            {
                mRID = $"msg-rsm004-{Guid.NewGuid():N}",
                type = "E44",
                MktActivityRecord = new
                {
                    MarketEvaluationPoint = new
                    {
                        mRID = "571313100000012345",
                        linkedMarketEvaluationPoint = new { mRID = "391" },
                        settlementMethod = "E02",
                    },
                    Period = new { timeInterval = new { start = "2025-03-01T00:00:00Z" } },
                },
            },
        };
        return JsonSerializer.Serialize(doc);
    }

    internal static string BuildRsm005ResponseJson(string correlationId, bool accepted, string? rejectCode = null, string? rejectMessage = null)
    {
        if (accepted)
        {
            var doc = new
            {
                MarketDocument = new
                {
                    mRID = correlationId,
                    MktActivityRecord = new
                    {
                        status = new { value = "A01" },
                    },
                },
            };
            return JsonSerializer.Serialize(doc);
        }
        else
        {
            var doc = new
            {
                MarketDocument = new
                {
                    mRID = correlationId,
                    MktActivityRecord = new
                    {
                        status = new { value = "A02" },
                        Reason = new { code = rejectCode ?? "E16", text = rejectMessage ?? "Rejected" },
                    },
                },
            };
            return JsonSerializer.Serialize(doc);
        }
    }

    internal static string BuildRsm001AcceptJson(string correlationId)
    {
        var doc = new
        {
            MarketDocument = new
            {
                mRID = correlationId,
                MktActivityRecord = new
                {
                    status = new { value = "A01" },
                },
            },
        };
        return JsonSerializer.Serialize(doc);
    }

    internal static string BuildRsm004D31Json(string gsrn, string effectiveDate)
    {
        var doc = new
        {
            MarketDocument = new
            {
                mRID = $"msg-rsm004-d31-{Guid.NewGuid():N}",
                type = "E44",
                MktActivityRecord = new
                {
                    MarketEvaluationPoint = new
                    {
                        mRID = gsrn,
                    },
                    Reason = new { code = Rsm004ReasonCodes.ForcedTransfer, text = "Overdragelse af målepunkt" },
                    Period = new { timeInterval = new { start = effectiveDate } },
                },
            },
        };
        return JsonSerializer.Serialize(doc);
    }

    private static void LoadForcedTransfer(SimulatorState state)
    {
        var gsrn = "571313100000012345";
        var effectiveDate = "2025-01-15T00:00:00Z";
        var correlationId = "corr-sim-forced";

        // RSM-004/D31: Transfer notification
        state.EnqueueMessage("MasterData", "RSM-004", correlationId,
            BuildRsm004D31Json(gsrn, effectiveDate));

        // RSM-022: Master data
        state.EnqueueMessage("MasterData", "RSM-022", correlationId,
            BuildRsm022Json(gsrn, effectiveDate));

        // RSM-028: Customer data (no CPR)
        state.EnqueueMessage("MasterData", "RSM-028", correlationId,
            BuildRsm028Json(gsrn, "Simulated Customer", "0000000000", includeCpr: false));

        // RSM-031: Price attachments
        state.EnqueueMessage("MasterData", "RSM-031", correlationId,
            BuildRsm031Json(gsrn, effectiveDate));

        // RSM-012: Metering data (retroactive)
        state.EnqueueMessage("Timeseries", "RSM-012", correlationId, BuildRsm012Json(
            new DateTimeOffset(2025, 1, 15, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2025, 2, 1, 0, 0, 0, TimeSpan.Zero),
            408));
    }
}
