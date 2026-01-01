namespace Cycon.Render;

public interface IMeasureBlock
{
    BlockSize Measure(in BlockMeasureContext ctx);
}

