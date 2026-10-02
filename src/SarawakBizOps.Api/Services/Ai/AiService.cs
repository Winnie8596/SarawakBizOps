using System.Diagnostics;
using Microsoft.Extensions.Options;
using SarawakBizOps.Api.DTOs.Ai;
using SarawakBizOps.Api.Services.Common;

namespace SarawakBizOps.Api.Services.Ai;

public class AiService : IAiService
{
    public const string TechnicianDisclaimer = "AI-generated assistance — not a certified diagnosis. " +
        "Check everything on site before acting; nothing here is applied to the job.";

    public const string UnavailableMessage =
        "The AI assistant is unavailable right now. Carry on with the job; you can try again later.";

    public const string ManagerDisclaimer = "AI-written summary of the figures beside it. The figures come straight " +
        "from the system's analytics; the summary may only repeat them — check them before acting.";

    public const string SummaryUnavailableMessage =
        "The AI summary is unavailable right now. The figures below come straight from the analytics.";

    private readonly TechnicianAssistantContextBuilder _contextBuilder;
    private readonly AiReadOnlyData _data;
    private readonly ILlmClient _llm;
    private readonly AiOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<AiService> _logger;

    public AiService(
        TechnicianAssistantContextBuilder contextBuilder, AiReadOnlyData data, ILlmClient llm, IOptions<AiOptions> options,
        TimeProvider clock, ILogger<AiService> logger)
    {
        _contextBuilder = contextBuilder;
        _data = data;
        _llm = llm;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ServiceResult<TechnicianAssistanceDto>> GetTechnicianAssistanceAsync(
        int workOrderId, string actingUserId, bool requireOwnTechnician, string? observations, CancellationToken ct)
    {
        var input = await _contextBuilder.BuildAsync(workOrderId, actingUserId, requireOwnTechnician, observations, ct);
        if (input is null)
        {
            return ServiceResult<TechnicianAssistanceDto>.Fail(ServiceErrorKind.NotFound, "Work order not found.");
        }

        var raw = await AskAsync(TechnicianAssistantPrompt.BuildRequest(input.Context), $"work order {workOrderId}", ct);
        if (raw is null)
        {
            return ServiceResult<TechnicianAssistanceDto>.Fail(ServiceErrorKind.Unavailable, UnavailableMessage);
        }

        var (answer, error) = TechnicianAssistantOutput.Validate(
            raw, input.PastCaseCompletedAtUtc.Keys.ToHashSet(), input.PartQuantityOnHand.Keys.ToHashSet());
        if (answer is null)
        {
            // The reason is ours (never the model's text), so it is safe to log.
            _logger.LogWarning("AI {Schema} answer for {Subject} rejected: {Reason}",
                TechnicianAssistantPrompt.SchemaName, $"work order {workOrderId}", error);
            return ServiceResult<TechnicianAssistanceDto>.Fail(ServiceErrorKind.Unavailable, UnavailableMessage);
        }

        var pastCases = input.Context.PastCases.ToDictionary(c => c.WorkOrderId);
        var parts = input.Context.CandidateParts.ToDictionary(p => p.PartId);

        return ServiceResult<TechnicianAssistanceDto>.Ok(new TechnicianAssistanceDto
        {
            WorkOrderId = workOrderId,
            Disclaimer = TechnicianDisclaimer,
            GeneratedBy = _llm.DisplayName,
            GeneratedAt = _clock.GetUtcNow().UtcDateTime,
            PastCasesConsidered = pastCases.Count,
            PossibleCauses = answer.PossibleCauses
                .Select(c => new AssistantCauseDto { Cause = c.Cause, Likelihood = c.Likelihood, Reasoning = c.Reasoning })
                .ToList(),
            SuggestedChecks = answer.SuggestedChecks.ToList(),
            RelevantPastCases = answer.RelevantPastCases.Select(r => new AssistantPastCaseDto
            {
                WorkOrderId = r.Id,
                CompletedAt = input.PastCaseCompletedAtUtc[r.Id],
                ProblemDescription = pastCases[r.Id].Problem,
                Diagnosis = pastCases[r.Id].Diagnosis,
                WhyRelevant = r.WhyRelevant
            }).ToList(),
            RelevantParts = answer.RelevantParts.Select(r => new AssistantPartDto
            {
                PartId = r.Id,
                PartNumber = parts[r.Id].PartNumber,
                Name = parts[r.Id].Name,
                Unit = parts[r.Id].Unit,
                QuantityOnHand = input.PartQuantityOnHand[r.Id],
                WhyRelevant = r.WhyRelevant
            }).ToList()
        });
    }

    public IReadOnlyList<ManagerInsightTopicDto> GetManagerInsightTopics()
        => ManagerInsightTopics.All.Select(ToDto).ToList();

    public async Task<ManagerInsightDto> AskManagerInsightsAsync(string question, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        question = question.Trim();
        var mapping = ManagerInsightsIntentMapper.Map(question, now);

        if (mapping.Topic is null)
        {
            _logger.LogInformation("AI {Schema} question refused by the mapper", ManagerInsightsPrompt.SchemaName);
            return new ManagerInsightDto
            {
                Outcome = ManagerInsightOutcomes.Refused,
                Question = question,
                GeneratedAt = now,
                RefusalReason = mapping.RefusalReason,
                SupportedTopics = GetManagerInsightTopics().ToList()
            };
        }

        // The allow-listed operation, through the read-only data access; these are the only numbers in the answer.
        var (facts, period) = await ManagerInsightFactsBuilder.BuildAsync(_data.Analytics, mapping.Topic, mapping.Period!, now, ct);
        var request = ManagerInsightsPrompt.BuildRequest(facts);
        var subject = $"topic {mapping.Topic.Id}";

        string? summary = null;
        var raw = await AskAsync(request, subject, ct);
        if (raw is not null)
        {
            (summary, var error) = ManagerInsightsOutput.Validate(raw, request.UserContent);
            if (summary is null)
            {
                _logger.LogWarning("AI {Schema} answer for {Subject} rejected: {Reason}",
                    ManagerInsightsPrompt.SchemaName, subject, error);
            }
        }

        return new ManagerInsightDto
        {
            Outcome = ManagerInsightOutcomes.Answered,
            Question = question,
            GeneratedAt = now,
            Topic = ToDto(mapping.Topic),
            PeriodLabel = facts.Period,
            Period = period,
            Definition = facts.Definition,
            Note = facts.Note,
            Figures = facts.Figures
                .Select(f => new ManagerInsightFigureDto { Label = f.Label, Value = f.Value, Unit = f.Unit })
                .ToList(),
            Summary = summary,
            SummaryUnavailableReason = summary is null ? SummaryUnavailableMessage : null,
            Disclaimer = ManagerDisclaimer,
            GeneratedBy = summary is null ? null : _llm.DisplayName
        };
    }

    private static ManagerInsightTopicDto ToDto(InsightTopic topic) => new()
    {
        Id = topic.Id,
        Title = topic.Title,
        Description = topic.Description,
        ExampleQuestion = topic.ExampleQuestion
    };

    /// <summary>Calls the model within the configured timeout. Returns null (after logging) on any failure. Only the
    /// schema name, the subject (a job id or a topic), timings, the exception type and our own <see cref="LlmProviderException"/> wording are
    /// logged — never the prompt, the answer, a provider's error body or the key.</summary>
    private async Task<string?> AskAsync(LlmRequest request, string subject, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
        var watch = Stopwatch.StartNew();

        try
        {
            var raw = await _llm.CompleteStructuredAsync(request, timeout.Token);
            _logger.LogInformation("AI {Schema} for {Subject} answered by {Provider} in {ElapsedMs} ms",
                request.SchemaName, subject, _llm.DisplayName, watch.ElapsedMilliseconds);
            return raw;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("AI {Schema} for {Subject} timed out after {ElapsedMs} ms ({Provider})",
                request.SchemaName, subject, watch.ElapsedMilliseconds, _llm.DisplayName);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("AI {Schema} for {Subject} failed with {ExceptionType}: {Reason} ({Provider})",
                request.SchemaName, subject, ex.GetType().Name,
                ex is LlmProviderException ? ex.Message : "(not logged)", _llm.DisplayName);
            return null;
        }
    }
}
