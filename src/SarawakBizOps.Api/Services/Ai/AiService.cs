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

    private readonly TechnicianAssistantContextBuilder _contextBuilder;
    private readonly ILlmClient _llm;
    private readonly AiOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<AiService> _logger;

    public AiService(
        TechnicianAssistantContextBuilder contextBuilder, ILlmClient llm, IOptions<AiOptions> options,
        TimeProvider clock, ILogger<AiService> logger)
    {
        _contextBuilder = contextBuilder;
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

        var raw = await AskAsync(TechnicianAssistantPrompt.BuildRequest(input.Context), workOrderId, ct);
        if (raw is null)
        {
            return ServiceResult<TechnicianAssistanceDto>.Fail(ServiceErrorKind.Unavailable, UnavailableMessage);
        }

        var (answer, error) = TechnicianAssistantOutput.Validate(
            raw, input.PastCaseCompletedAtUtc.Keys.ToHashSet(), input.PartQuantityOnHand.Keys.ToHashSet());
        if (answer is null)
        {
            // The reason is ours (never the model's text), so it is safe to log.
            _logger.LogWarning("AI {Schema} answer for work order {WorkOrderId} rejected: {Reason}",
                TechnicianAssistantPrompt.SchemaName, workOrderId, error);
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

    /// <summary>Calls the model within the configured timeout. Returns null (after logging) on any failure. Only the
    /// schema name, the job id, timings, the exception type and our own <see cref="LlmProviderException"/> wording are
    /// logged — never the prompt, the answer, a provider's error body or the key.</summary>
    private async Task<string?> AskAsync(LlmRequest request, int workOrderId, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds)));
        var watch = Stopwatch.StartNew();

        try
        {
            var raw = await _llm.CompleteStructuredAsync(request, timeout.Token);
            _logger.LogInformation("AI {Schema} for work order {WorkOrderId} answered by {Provider} in {ElapsedMs} ms",
                request.SchemaName, workOrderId, _llm.DisplayName, watch.ElapsedMilliseconds);
            return raw;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("AI {Schema} for work order {WorkOrderId} timed out after {ElapsedMs} ms ({Provider})",
                request.SchemaName, workOrderId, watch.ElapsedMilliseconds, _llm.DisplayName);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning("AI {Schema} for work order {WorkOrderId} failed with {ExceptionType}: {Reason} ({Provider})",
                request.SchemaName, workOrderId, ex.GetType().Name,
                ex is LlmProviderException ? ex.Message : "(not logged)", _llm.DisplayName);
            return null;
        }
    }
}
