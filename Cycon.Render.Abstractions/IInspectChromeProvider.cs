namespace Cycon.Render;

public interface IInspectChromeProvider
{
    InspectChromeSpec GetInspectChromeSpec();

    void PopulateInspectChromeData(ref InspectChromeDataBuilder b);
}
