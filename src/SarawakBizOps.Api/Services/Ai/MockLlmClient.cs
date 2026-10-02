using System.Text.Json;

namespace SarawakBizOps.Api.Services.Ai;

/// <summary>
/// The default "model" in development, tests and the Docker demo (PRD §6.10): deterministic, free and offline. It reads
/// the same JSON context a real model would get and answers in the same schema, from a small keyword table — so the
/// whole pipeline (context, validation, grounding, UI) runs for real, and the same question always gets the same
/// answer. It is not intelligent and does not pretend to be: the UI names it as the demo assistant.
/// </summary>
public class MockLlmClient : ILlmClient
{
    public string DisplayName => "Demo mock assistant";

    public Task<string> CompleteStructuredAsync(LlmRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return request.SchemaName switch
        {
            TechnicianAssistantPrompt.SchemaName => Task.FromResult(MockTechnicianAssistant.Answer(request.UserContent)),
            _ => throw new NotSupportedException($"The mock assistant has no answer for '{request.SchemaName}'.")
        };
    }
}

/// <summary>The mock's technician answers: keyword rules over the problem, the saved diagnosis and the notes.</summary>
public static class MockTechnicianAssistant
{
    private record Rule(string[] Keywords, string Cause, string[] Checks, string[] PartWords);

    // Order matters: the earlier a matching rule, the more likely its cause is reported.
    private static readonly Rule[] Rules =
    {
        new(new[] { "trip", "overload", "relay", "short-cycl", "cycles" },
            "Motor protection tripping: overload relay set too low, pitted contacts or the motor drawing excess current",
            new[]
            {
                "Measure the running current on each phase and compare it with the motor nameplate.",
                "Check the overload relay setting against the motor's full-load current.",
                "Inspect contactor and relay contacts for pitting or burning."
            },
            new[] { "relay", "fuse" }),
        new(new[] { "leak", "seal", "drip", "fluid" },
            "Worn or damaged seal letting fluid past",
            new[]
            {
                "Clean the area, run the unit briefly and trace the leak to its source.",
                "Inspect the seal faces and shaft sleeve for scoring or wear.",
                "Check the fluid level and top up to specification after the repair."
            },
            new[] { "seal" }),
        new(new[] { "noise", "noisy", "vibrat", "grinding", "bearing" },
            "Worn, dry or misaligned bearings",
            new[]
            {
                "Listen at each bearing housing (stethoscope or screwdriver) with the unit running.",
                "Check bearing housing temperature and look for discoloured grease.",
                "Check shaft and coupling alignment and look for loose mounting bolts."
            },
            new[] { "bearing" }),
        new(new[] { "overheat", "running hot", "temperature", "head pressure" },
            "Restricted cooling or lubrication",
            new[]
            {
                "Check coolers, coils and fans for dust or blockage.",
                "Check the lubricant level and condition.",
                "Compare the temperature reading with an independent thermometer."
            },
            new[] { "oil", "filter", "sensor" }),
        new(new[] { "voltage", "avr", "power", "start", "starter" },
            "Electrical supply or regulation fault",
            new[]
            {
                "Check terminals and sensing leads for looseness or corrosion.",
                "Measure the supply voltage at the unit under load."
            },
            new[] { "voltage", "regulator", "relay" }),
        new(new[] { "pressure", "flow", "prime", "block", "clog" },
            "Blockage or restriction in the flow path",
            new[]
            {
                "Inspect strainers, filters and the suction line for blockage.",
                "Compare the pressure readings with the equipment's rated values."
            },
            new[] { "filter", "strainer" }),
        new(new[] { "belt", "tracking", "drift", "conveyor", "splice", "blade" },
            "Belt or blade tension and tracking out of adjustment, or a worn belt",
            new[]
            {
                "Check belt or blade tension and tracking after warm-up.",
                "Inspect the belt splice and the idler and guide bearings."
            },
            new[] { "belt", "blade", "bearing" }),
        new(new[] { "sensor", "reading", "gauge", "erratic" },
            "Faulty sensor or a loose sensing lead",
            new[]
            {
                "Check the sensor wiring and connections.",
                "Compare the reading with a calibrated reference instrument."
            },
            new[] { "sensor" })
    };

    private static readonly string[] LikelihoodByRank = { "High", "Medium", "Low" };

    public static string Answer(string contextJson)
    {
        var context = JsonSerializer.Deserialize<TechnicianAssistantContext>(contextJson, TechnicianAssistantPrompt.ContextJson)
            ?? throw new InvalidOperationException("The mock assistant received no context.");

        var text = $"{context.Job.ProblemDescription} {context.Job.DiagnosisSoFar} {context.Job.TechnicianObservations}"
            .ToLowerInvariant();
        var matched = Rules
            .Select(rule => (Rule: rule, Hit: rule.Keywords.FirstOrDefault(text.Contains)))
            .Where(m => m.Hit is not null)
            .Take(3)
            .ToList();

        var causes = matched.Select((m, rank) => new
        {
            cause = m.Rule.Cause,
            likelihood = LikelihoodByRank[rank],
            reasoning = $"The job mentions “{m.Hit}”."
        }).ToList();
        var checks = matched.SelectMany(m => m.Rule.Checks).Distinct().Take(6).ToList();

        if (causes.Count == 0)
        {
            causes.Add(new
            {
                cause = "Not clear from the description",
                likelihood = "Low",
                reasoning = "No known symptom words were found; start with a general inspection."
            });
            checks.Add("Run the unit and note exactly when and how the fault shows.");
            checks.Add("Check for loose connections, leaks and unusual noise or heat.");
        }

        // Earlier jobs that share a symptom word with this one; otherwise the most recent job as background.
        var keywords = matched.SelectMany(m => m.Rule.Keywords).ToArray();
        var pastCases = context.PastCases
            .Select(c => (Case: c, Shared: keywords.FirstOrDefault($"{c.Problem} {c.Diagnosis}".ToLowerInvariant().Contains)))
            .Where(c => c.Shared is not null)
            .Take(3)
            .Select(c => new { workOrderId = c.Case.WorkOrderId, whyRelevant = $"Also involved “{c.Shared}” on this equipment." })
            .ToList();
        if (pastCases.Count == 0 && context.PastCases.Count > 0)
        {
            pastCases.Add(new { workOrderId = context.PastCases[0].WorkOrderId, whyRelevant = "The most recent job on this equipment." });
        }
        if (context.PastCases.Count > 0)
        {
            checks.Add("Compare with the earlier jobs on this equipment below.");
        }

        var partWords = matched.SelectMany(m => m.Rule.PartWords).Distinct().ToArray();
        var parts = context.CandidateParts
            .Select(p => (Part: p, Word: partWords.FirstOrDefault($"{p.Name} {p.Description}".ToLowerInvariant().Contains)))
            .Where(p => p.Word is not null)
            .Take(3)
            .Select(p => new { partId = p.Part.PartId, whyRelevant = $"A likely repair uses a {p.Word}." })
            .ToList();

        return JsonSerializer.Serialize(new
        {
            possibleCauses = causes,
            suggestedChecks = checks,
            relevantPastCases = pastCases,
            relevantParts = parts
        });
    }
}
