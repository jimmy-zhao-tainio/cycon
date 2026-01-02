using System;
using System.Collections.Generic;

namespace Extensions.Math.Impl;

internal sealed class MathEnv
{
    public MathEnv()
    {
        Variables = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        Ans = 0d;
    }

    public Dictionary<string, double> Variables { get; }

    public double Ans { get; set; }
}

