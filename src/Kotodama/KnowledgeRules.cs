namespace Kotodama;

/// <summary>Knowledge Candidate に決定論的な規則を適用します。</summary>
public sealed class KnowledgeRules
{
    /// <summary>Candidate を検証します。</summary>
    public static string? Validate(ClaimCandidate candidate, bool allowStrength)
    {
        if (candidate.SubjectId <= 0 || candidate.ObjectId <= 0) return "subject_id and object_id must be positive";
        if (!double.IsFinite(candidate.Confidence) || candidate.Confidence is < 0 or > 1) return "confidence must be between 0 and 1";
        if (candidate.AttributionConfidence is double attribution && (!double.IsFinite(attribution) || attribution is < 0 or > 1)) return "attribution_confidence must be between 0 and 1";
        if (candidate.Strength is double strength && (!double.IsFinite(strength) || strength is < 0 or > 1)) return "strength must be between 0 and 1";
        if (candidate.RelationType is "equals" or "canonical_of" && candidate.Polarity == Polarity.Negative) return "equals/canonical_of does not allow Negative polarity";
        if (candidate.Strength is not null && !allowStrength) return $"{candidate.RelationType} does not support strength";
        if (candidate.ValidFrom is not null && candidate.ValidTo < candidate.ValidFrom) return "valid_to must not precede valid_from";
        return null;
    }
}
