using System;
using System.Diagnostics;

namespace Cycon.Host.Hosting;

internal enum PromptCaretMode
{
    Hidden = 0,
    UnfocusedParked = 1,
    FocusedPulse = 2,
    TypingSolid = 3,
}

internal readonly record struct CaretPulseSettings(
    int PeriodMs,
    int FadeInMs,
    int OnHoldMs,
    int FadeOutMs,
    int OffHoldMs,
    double RisePow,
    double FallPow,
    byte MaxAlpha,
    int AnimHzDuringFade)
{
    public void Validate()
    {
        if (PeriodMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(PeriodMs));
        }

        if (FadeInMs < 0 || OnHoldMs < 0 || FadeOutMs < 0 || OffHoldMs < 0)
        {
            throw new ArgumentOutOfRangeException("Caret pulse segment durations must be non-negative.");
        }

        if (FadeInMs + OnHoldMs + FadeOutMs + OffHoldMs != PeriodMs)
        {
            throw new InvalidOperationException("CaretPulseSettings invariant failed: FadeIn+OnHold+FadeOut+OffHold must equal PeriodMs.");
        }

        if (RisePow <= 0 || FallPow <= 0)
        {
            throw new ArgumentOutOfRangeException("Caret pulse shaping exponents must be > 0.");
        }

        if (AnimHzDuringFade <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(AnimHzDuringFade));
        }
    }
}

internal readonly record struct CaretFocusSettings(
    byte ParkedAlpha,
    int BlurFadeMs,
    int FocusWakeFadeMs,
    int FocusWakeHoldMs)
{
    public void Validate()
    {
        if (BlurFadeMs < 0 || FocusWakeFadeMs < 0 || FocusWakeHoldMs < 0)
        {
            throw new ArgumentOutOfRangeException("Caret focus transition durations must be non-negative.");
        }
    }
}

internal readonly record struct CaretTypingSettings(
    int TypingGraceMs,
    byte SolidAlpha)
{
    public void Validate()
    {
        if (TypingGraceMs < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TypingGraceMs));
        }
    }
}

internal readonly record struct PromptCaretSettings(
    CaretPulseSettings Pulse,
    CaretFocusSettings Focus,
    CaretTypingSettings Typing)
{
    public void Validate()
    {
        Pulse.Validate();
        Focus.Validate();
        Typing.Validate();
    }

    public static PromptCaretSettings Default => new(
        Pulse: new CaretPulseSettings(
            PeriodMs: 1500,
            FadeInMs: 180,
            OnHoldMs: 670,
            FadeOutMs: 400,
            OffHoldMs: 250,
            RisePow: 0.75,
            FallPow: 1.55,
            MaxAlpha: 0xFF,
            AnimHzDuringFade: 30),
        Focus: new CaretFocusSettings(
            ParkedAlpha: 104,
            BlurFadeMs: 120,
            FocusWakeFadeMs: 90,
            FocusWakeHoldMs: 100),
        Typing: new CaretTypingSettings(
            TypingGraceMs: 500,
            SolidAlpha: 0xFF));
}

internal static class PromptCaretMath
{
    public static byte ComputePulseAlpha(long nowTicks, in CaretPulseSettings s, long epochTicks)
    {
        if (s.PeriodMs <= 0)
        {
            return 0;
        }

        var periodTicks = MsToTicks(s.PeriodMs);
        if (periodTicks <= 0)
        {
            return 0;
        }

        var elapsed = nowTicks - epochTicks;
        if (elapsed < 0)
        {
            elapsed = 0;
        }

        var phaseTicks = elapsed % periodTicks;

        var offHoldEnd = MsToTicks(s.OffHoldMs);
        var fadeInEnd = offHoldEnd + MsToTicks(s.FadeInMs);
        var onHoldEnd = fadeInEnd + MsToTicks(s.OnHoldMs);
        var fadeOutEnd = onHoldEnd + MsToTicks(s.FadeOutMs);

        static double SmootherStep(double u)
        {
            u = Math.Clamp(u, 0.0, 1.0);
            return u * u * u * (u * (u * 6.0 - 15.0) + 10.0);
        }

        static double Pow01(double x, double p)
        {
            if (x <= 0.0)
            {
                return 0.0;
            }

            if (x >= 1.0)
            {
                return 1.0;
            }

            return Math.Pow(x, p);
        }

        double a;
        if (phaseTicks < offHoldEnd)
        {
            a = 0.0;
        }
        else if (phaseTicks < fadeInEnd)
        {
            var u = (phaseTicks - offHoldEnd) / (double)Math.Max(1, fadeInEnd - offHoldEnd);
            a = Pow01(SmootherStep(u), s.RisePow);
        }
        else if (phaseTicks < onHoldEnd)
        {
            a = 1.0;
        }
        else if (phaseTicks < fadeOutEnd)
        {
            var u = (phaseTicks - onHoldEnd) / (double)Math.Max(1, fadeOutEnd - onHoldEnd);
            a = Pow01(1.0 - SmootherStep(u), s.FallPow);
        }
        else
        {
            a = 0.0;
        }

        return (byte)Math.Clamp((int)Math.Round(a * s.MaxAlpha), 0, 255);
    }

    public static long ComputeNextPulseDeadlineTicks(long nowTicks, in CaretPulseSettings s, long epochTicks)
    {
        var periodTicks = MsToTicks(s.PeriodMs);
        if (periodTicks <= 0)
        {
            return long.MaxValue;
        }

        var animIntervalTicks = HzToTicks(s.AnimHzDuringFade);

        var elapsed = nowTicks - epochTicks;
        if (elapsed < 0)
        {
            elapsed = 0;
        }

        var cycleIndex = elapsed / periodTicks;
        var cycleStart = epochTicks + cycleIndex * periodTicks;
        var phaseTicks = nowTicks - cycleStart;

        var offHoldEnd = MsToTicks(s.OffHoldMs);
        var fadeInEnd = offHoldEnd + MsToTicks(s.FadeInMs);
        var onHoldEnd = fadeInEnd + MsToTicks(s.OnHoldMs);
        var fadeOutEnd = onHoldEnd + MsToTicks(s.FadeOutMs);

        long nextEdge;
        bool inFade;
        if (phaseTicks < offHoldEnd)
        {
            nextEdge = cycleStart + offHoldEnd;
            inFade = false;
        }
        else if (phaseTicks < fadeInEnd)
        {
            nextEdge = cycleStart + fadeInEnd;
            inFade = true;
        }
        else if (phaseTicks < onHoldEnd)
        {
            nextEdge = cycleStart + onHoldEnd;
            inFade = false;
        }
        else if (phaseTicks < fadeOutEnd)
        {
            nextEdge = cycleStart + fadeOutEnd;
            inFade = true;
        }
        else
        {
            nextEdge = cycleStart + periodTicks + offHoldEnd;
            inFade = false;
        }

        if (!inFade)
        {
            return nextEdge;
        }

        // Sample at a stable rate during fades, aligned to the epoch to avoid drift.
        var nextSample = epochTicks + ((elapsed / animIntervalTicks) + 1) * animIntervalTicks;
        return Math.Min(nextSample, nextEdge);
    }

    public static long MsToTicks(int ms) =>
        (long)Math.Round((double)ms * Stopwatch.Frequency / 1000.0);

    public static long HzToTicks(int hz)
    {
        hz = Math.Max(1, hz);
        return (long)Math.Round(Stopwatch.Frequency / (double)hz);
    }
}

internal sealed class PromptCaretController
{
    private PromptCaretSettings _settings = PromptCaretSettings.Default;
    private PromptCaretMode _mode = PromptCaretMode.FocusedPulse;

    private bool _promptFocused = true;
    private bool _suppressed;

    private long _pulseEpochTicks;
    private long _typingUntilTicks;

    private long _blurFadeStartTicks;
    private long _blurFadeEndTicks;
    private byte _blurFadeFromAlpha;

    private long _focusWakeStartTicks;
    private long _focusWakeFadeEndTicks;
    private long _focusWakeHoldEndTicks;

    private long _nextDeadlineTicks = long.MaxValue;

    public PromptCaretController()
    {
        _settings.Validate();
        var now = Stopwatch.GetTimestamp();
        _pulseEpochTicks = now;
        _nextDeadlineTicks = PromptCaretMath.ComputeNextPulseDeadlineTicks(now, _settings.Pulse, _pulseEpochTicks);
    }

    public PromptCaretSettings Settings => _settings;

    public PromptCaretMode Mode => _mode;

    public long NextDeadlineTicks => _nextDeadlineTicks;

    public void SetSettings(in PromptCaretSettings settings)
    {
        settings.Validate();
        _settings = settings;
    }

    public void SetSuppressed(bool suppressed, long nowTicks)
    {
        _suppressed = suppressed;
        if (_suppressed)
        {
            _nextDeadlineTicks = long.MaxValue;
        }
        else
        {
            _nextDeadlineTicks = ComputeNextDeadline(nowTicks);
        }
    }

    public void OnTyped(long nowTicks)
    {
        if (!_promptFocused || _suppressed)
        {
            return;
        }

        _mode = PromptCaretMode.TypingSolid;
        _typingUntilTicks = nowTicks + PromptCaretMath.MsToTicks(_settings.Typing.TypingGraceMs);
        _nextDeadlineTicks = _typingUntilTicks;
    }

    public void SetPromptFocused(bool focused, long nowTicks)
    {
        if (_promptFocused == focused)
        {
            return;
        }

        var previousAlpha = SampleAlpha(nowTicks);
        _promptFocused = focused;

        if (!_promptFocused)
        {
            _mode = PromptCaretMode.UnfocusedParked;
            StartBlurFade(previousAlpha, nowTicks);
            _nextDeadlineTicks = ComputeNextDeadline(nowTicks);
            return;
        }

        // Prompt regained focus: deterministic wake, then pulse at a known phase start.
        _mode = PromptCaretMode.FocusedPulse;
        StartFocusWake(nowTicks);
        _typingUntilTicks = 0;
        _nextDeadlineTicks = ComputeNextDeadline(nowTicks);
    }

    public byte SampleAlpha(long nowTicks)
    {
        if (_suppressed || _mode == PromptCaretMode.Hidden)
        {
            return 0;
        }

        if (!_promptFocused && _mode != PromptCaretMode.UnfocusedParked)
        {
            return 0;
        }

        if (_mode == PromptCaretMode.UnfocusedParked)
        {
            if (_blurFadeEndTicks != 0 && nowTicks < _blurFadeEndTicks)
            {
                var u = (nowTicks - _blurFadeStartTicks) / (double)Math.Max(1, _blurFadeEndTicks - _blurFadeStartTicks);
                u = Math.Clamp(u, 0.0, 1.0);
                var eased = u * u * u * (u * (u * 6.0 - 15.0) + 10.0);
                var a = _blurFadeFromAlpha + (int)Math.Round((_settings.Focus.ParkedAlpha - _blurFadeFromAlpha) * eased);
                return (byte)Math.Clamp(a, 0, 255);
            }

            return _settings.Focus.ParkedAlpha;
        }

        if (_mode == PromptCaretMode.TypingSolid)
        {
            return _settings.Typing.SolidAlpha;
        }

        // FocusedPulse.
        if (_focusWakeHoldEndTicks != 0 && nowTicks < _focusWakeHoldEndTicks)
        {
            if (nowTicks < _focusWakeFadeEndTicks)
            {
                var u = (nowTicks - _focusWakeStartTicks) / (double)Math.Max(1, _focusWakeFadeEndTicks - _focusWakeStartTicks);
                u = Math.Clamp(u, 0.0, 1.0);
                var eased = u * u * u * (u * (u * 6.0 - 15.0) + 10.0);
                return (byte)Math.Clamp((int)Math.Round(eased * _settings.Pulse.MaxAlpha), 0, 255);
            }

            return _settings.Pulse.MaxAlpha;
        }

        return PromptCaretMath.ComputePulseAlpha(nowTicks, _settings.Pulse, _pulseEpochTicks);
    }

    public void Update(long nowTicks)
    {
        if (_suppressed)
        {
            _nextDeadlineTicks = long.MaxValue;
            return;
        }

        if (_mode == PromptCaretMode.TypingSolid && _typingUntilTicks != 0 && nowTicks >= _typingUntilTicks)
        {
            // Deterministic re-entry: start pulse at OnHold start (solid).
            _mode = PromptCaretMode.FocusedPulse;
            _typingUntilTicks = 0;
            SetPulseEpochForOnHoldStart(nowTicks);
        }

        if (_mode == PromptCaretMode.UnfocusedParked && _blurFadeEndTicks != 0 && nowTicks >= _blurFadeEndTicks)
        {
            _blurFadeEndTicks = 0;
        }

        if (_mode == PromptCaretMode.FocusedPulse && _focusWakeHoldEndTicks != 0 && nowTicks >= _focusWakeHoldEndTicks)
        {
            _focusWakeHoldEndTicks = 0;
        }

        _nextDeadlineTicks = ComputeNextDeadline(nowTicks);
    }

    private void StartBlurFade(byte fromAlpha, long nowTicks)
    {
        var ms = _settings.Focus.BlurFadeMs;
        if (ms <= 0)
        {
            _blurFadeStartTicks = 0;
            _blurFadeEndTicks = 0;
            _blurFadeFromAlpha = 0;
            return;
        }

        _blurFadeFromAlpha = fromAlpha;
        _blurFadeStartTicks = nowTicks;
        _blurFadeEndTicks = nowTicks + PromptCaretMath.MsToTicks(ms);
    }

    private void StartFocusWake(long nowTicks)
    {
        var fadeMs = _settings.Focus.FocusWakeFadeMs;
        var holdMs = _settings.Focus.FocusWakeHoldMs;
        _focusWakeStartTicks = nowTicks;
        _focusWakeFadeEndTicks = nowTicks + PromptCaretMath.MsToTicks(fadeMs);
        _focusWakeHoldEndTicks = _focusWakeFadeEndTicks + PromptCaretMath.MsToTicks(holdMs);

        // At the moment the wake completes, start pulse at OnHold start for continuity.
        SetPulseEpochForOnHoldStart(_focusWakeHoldEndTicks);
    }

    private void SetPulseEpochForOnHoldStart(long atTicks)
    {
        var offsetMs = _settings.Pulse.OffHoldMs + _settings.Pulse.FadeInMs;
        _pulseEpochTicks = atTicks - PromptCaretMath.MsToTicks(offsetMs);
    }

    private long ComputeNextDeadline(long nowTicks)
    {
        if (_suppressed || _mode == PromptCaretMode.Hidden)
        {
            return long.MaxValue;
        }

        if (!_promptFocused)
        {
            if (_mode != PromptCaretMode.UnfocusedParked)
            {
                return long.MaxValue;
            }

            if (_blurFadeEndTicks != 0 && nowTicks < _blurFadeEndTicks)
            {
                var interval = PromptCaretMath.HzToTicks(_settings.Pulse.AnimHzDuringFade);
                var elapsed = nowTicks - _blurFadeStartTicks;
                if (elapsed < 0)
                {
                    elapsed = 0;
                }

                var nextSample = _blurFadeStartTicks + ((elapsed / interval) + 1) * interval;
                return Math.Min(nextSample, _blurFadeEndTicks);
            }

            return long.MaxValue;
        }

        if (_mode == PromptCaretMode.TypingSolid)
        {
            return _typingUntilTicks == 0 ? long.MaxValue : _typingUntilTicks;
        }

        // FocusedPulse.
        if (_focusWakeHoldEndTicks != 0 && nowTicks < _focusWakeHoldEndTicks)
        {
            if (nowTicks < _focusWakeFadeEndTicks)
            {
                var interval = PromptCaretMath.HzToTicks(_settings.Pulse.AnimHzDuringFade);
                var elapsed = nowTicks - _focusWakeStartTicks;
                if (elapsed < 0)
                {
                    elapsed = 0;
                }

                var nextSample = _focusWakeStartTicks + ((elapsed / interval) + 1) * interval;
                return Math.Min(nextSample, _focusWakeFadeEndTicks);
            }

            return _focusWakeHoldEndTicks;
        }

        return PromptCaretMath.ComputeNextPulseDeadlineTicks(nowTicks, _settings.Pulse, _pulseEpochTicks);
    }
}
