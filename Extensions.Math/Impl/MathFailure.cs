namespace Extensions.Math.Impl;

internal readonly record struct MathFailure(MathFailureKind Kind, string Message)
{
    public static MathFailure FromKind(MathFailureKind kind) =>
        new(kind, kind switch
        {
            MathFailureKind.DivideByZero => "Divide by zero.",
            MathFailureKind.Overflow => "Result out of range.",
            MathFailureKind.Invalid => "Invalid numeric result.",
            _ => "Invalid numeric result."
        });

    public static MathFailure Invalid(string message) => new(MathFailureKind.Invalid, message);
}

