namespace SarawakBizOps.Api.DTOs.Ai;

/// <summary>
/// The technician assistant's answer (AI-01): suggestions only, never applied to the job. <see cref="Disclaimer"/> is
/// set by the server on every answer — it is not left to the model to remember.
/// </summary>
public class TechnicianAssistanceDto
{
    public int WorkOrderId { get; set; }
    public string Disclaimer { get; set; } = string.Empty;
    /// <summary>Where the answer came from, e.g. "Demo mock assistant" or the configured model's name.</summary>
    public string GeneratedBy { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    /// <summary>How many earlier jobs on this equipment the assistant was shown (0 = none on record).</summary>
    public int PastCasesConsidered { get; set; }

    public List<AssistantCauseDto> PossibleCauses { get; set; } = new();
    public List<string> SuggestedChecks { get; set; } = new();
    public List<AssistantPastCaseDto> RelevantPastCases { get; set; } = new();
    public List<AssistantPartDto> RelevantParts { get; set; } = new();
}

public class AssistantCauseDto
{
    public string Cause { get; set; } = string.Empty;
    /// <summary>High, Medium or Low.</summary>
    public string Likelihood { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
}

/// <summary>An earlier job on the same equipment; the facts come from the database, only the reason from the model.</summary>
public class AssistantPastCaseDto
{
    public int WorkOrderId { get; set; }
    public DateTime CompletedAt { get; set; }
    public string ProblemDescription { get; set; } = string.Empty;
    public string Diagnosis { get; set; } = string.Empty;
    public string WhyRelevant { get; set; } = string.Empty;
}

/// <summary>A catalogue part; the facts (and the stock on hand) come from the database, only the reason from the model.</summary>
public class AssistantPartDto
{
    public int PartId { get; set; }
    public string PartNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal QuantityOnHand { get; set; }
    public string WhyRelevant { get; set; } = string.Empty;
}
