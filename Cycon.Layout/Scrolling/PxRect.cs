namespace Cycon.Layout.Scrolling;

public readonly record struct PxRect(int X, int Y, int Width, int Height)
{
    public bool Contains(int x, int y)
    {
        if (Width <= 0 || Height <= 0)
        {
            return false;
        }

        return x >= X && x < X + Width && y >= Y && y < Y + Height;
    }
}

