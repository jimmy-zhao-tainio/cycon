using System;
using System.Runtime.InteropServices;
using Cycon.Backends.Abstractions;

namespace Cycon.Backends.SilkNet;

public sealed class SilkClipboard : IClipboard
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    public void SetText(string text)
    {
        text ??= string.Empty;

        if (!OpenClipboard(IntPtr.Zero))
        {
            return;
        }

        try
        {
            EmptyClipboard();

            var bytes = (text + "\0").ToCharArray();
            var sizeBytes = bytes.Length * sizeof(char);
            var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (nuint)sizeBytes);
            if (hGlobal == IntPtr.Zero)
            {
                return;
            }

            var locked = GlobalLock(hGlobal);
            if (locked == IntPtr.Zero)
            {
                GlobalFree(hGlobal);
                return;
            }

            try
            {
                Marshal.Copy(bytes, 0, locked, bytes.Length);
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
            {
                GlobalFree(hGlobal);
            }
            // On success, the system owns hGlobal.
        }
        finally
        {
            CloseClipboard();
        }
    }

    public string? GetText()
    {
        if (!OpenClipboard(IntPtr.Zero))
        {
            return null;
        }

        try
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            var locked = GlobalLock(handle);
            if (locked == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(locked);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);
}
