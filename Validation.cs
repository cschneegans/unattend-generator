using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Schneegans.Unattend;

public static class Validation
{
  public static T NotNull<T>([NotNull] T? property, [CallerArgumentExpression(nameof(property))] string field = "") where T : struct
  {
    if (property == null)
    {
      throw new ConfigurationException($"Parameter '{field}' must be set.");
    }
    else
    {
      return (T)property;
    }
  }

  public static string StringNotEmpty([NotNull] string? property, [CallerArgumentExpression(nameof(property))] string field = "")
  {
    if (string.IsNullOrEmpty(property))
    {
      throw new ConfigurationException($"Parameter '{field}' must be set.");
    }
    else
    {
      return property;
    }
  }

  [return: NotNull]
  public static int InRange([NotNull] int? property, int? min = null, int? max = null, [CallerArgumentExpression(nameof(property))] string field = "")
  {
    NotNull(property, field);

    if (min.HasValue)
    {
      if (property < min)
      {
        throw new ConfigurationException($"Value of parameter '{field}' must not be less than {min}, but was {property}.");
      }
    }
    if (max.HasValue)
    {
      if (property > max)
      {
        throw new ConfigurationException($"Value of parameter '{field}' must not be greater than {max}, but was {property}.");
      }
    }

    return (int)property;
  }
}