namespace Extensions.Math.Impl;

internal interface IMathEngine
{
    MathTryResult TryEvaluate(string input, MathEnv env, out MathValueResult result, out MathFailure failure);
}
