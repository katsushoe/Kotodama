using FluentAssertions;
using Xunit;

namespace Kotodama.Tests;

public sealed class KnowledgeRulesTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public void Validate_WhenEntityIdIsNotPositive_ReturnsReason(long subjectId, long objectId)
    {
        var result = KnowledgeRules.Validate(new(subjectId, objectId, "related_to"), true);

        result.Should().Be("subject_id and object_id must be positive");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Validate_WhenConfidenceIsOutOfRange_ReturnsReason(double confidence)
    {
        var result = KnowledgeRules.Validate(new(1, 2, "related_to", Confidence: confidence), true);

        result.Should().Be("confidence must be between 0 and 1");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Validate_WhenAttributionConfidenceIsOutOfRange_ReturnsReason(double confidence)
    {
        var result = KnowledgeRules.Validate(new(1, 2, "related_to", AttributionConfidence: confidence), true);

        result.Should().Be("attribution_confidence must be between 0 and 1");
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Validate_WhenStrengthIsOutOfRange_ReturnsReason(double strength)
    {
        var result = KnowledgeRules.Validate(new(1, 2, "related_to", Strength: strength), true);

        result.Should().Be("strength must be between 0 and 1");
    }

    [Fact]
    public void Validate_WhenStrengthIsNotAllowed_ReturnsReason()
    {
        var result = KnowledgeRules.Validate(new(1, 2, "parent_of", Strength: 0.5), false);

        result.Should().Be("parent_of does not support strength");
    }

    [Fact]
    public void Validate_WhenValidToPrecedesValidFrom_ReturnsReason()
    {
        var from = DateTimeOffset.Parse("2026-08-25T00:00:00Z");
        var result = KnowledgeRules.Validate(new(1, 2, "related_to", ValidFrom: from, ValidTo: from.AddTicks(-1)), true);

        result.Should().Be("valid_to must not precede valid_from");
    }

    [Fact]
    public void Validate_WhenBoundaryValuesAreValid_ReturnsNull()
    {
        var at = DateTimeOffset.Parse("2026-08-25T00:00:00Z");
        var candidate = new ClaimCandidate(1, 2, "related_to", Confidence: 0, AttributionConfidence: 1, Strength: 0, ValidFrom: at, ValidTo: at);

        KnowledgeRules.Validate(candidate, true).Should().BeNull();
    }
}
