namespace Extensions.Math.Impl;

internal readonly record struct MathValueResult(bool IsAssignment, string? AssignedName, double Value, string DisplayText);

