namespace Cycon.Host.Ai;

public interface IAiStreamSink
{
    void Status(string shortStatus);
    void TextDelta(string delta);
    void Completed();
    void Error(string message);
}

