using System.Text.Json;

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
            default:
                throw new ArgumentException($"Unknown scenario: {scenarioName}");
        }
    }

    private static void LoadSunshine(SimulatorState state)
    {
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

    private static void LoadFullLifecycle(SimulatorState state)
    {
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

    private static string BuildRsm012Json(DateTimeOffset start, DateTimeOffset end, int hours)
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

        var doc = new
        {
            MarketDocument = new
            {
                mRID = $"msg-rsm012-{Guid.NewGuid():N}",
                Series = new[]
                {
                    new
                    {
                        mRID = $"txn-{Guid.NewGuid():N}",
                        MarketEvaluationPoint = new { mRID = "571313100000012345", type = "E17" },
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
}
