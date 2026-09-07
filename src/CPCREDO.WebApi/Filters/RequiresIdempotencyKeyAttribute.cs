namespace CPCREDO.WebApi.Filters;

/// <summary>
/// Marks a money POST action as requiring the Idempotency-Key header.
/// Apply this to every future endpoint that posts cash, journals, savings, or loans.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequiresIdempotencyKeyAttribute : Attribute
{
}
