namespace CQRS.Models;

public record SkillSummary(
    string SkillId,
    string Name,
    string Description,
    string CategoryName,
    string ProficiencyLevel,
    bool IsOffered,
    double? AverageRating = null);
