using System;

namespace GalNet.Editor.Controls;

public static class GraphLayoutMetrics
{
    public const double NodeWidth = 188;
    public const double NodeMinHeight = 96;
    public const double NodeHorizontalPadding = 8;
    public const double ConnectorHitSize = 18;
    public const double ConnectorSlotHeight = 26;
    public const double ConnectorVerticalMargin = 4;
    public const double ConnectorColumnGap = 6;

    public static double GetNodeHeight(int inputCount, int outputCount)
    {
        var connectorRows = Math.Max(inputCount, outputCount);
        var connectorHeight = connectorRows * (ConnectorHitSize + ConnectorVerticalMargin * 2);
        return Math.Max(NodeMinHeight, connectorHeight + NodeHorizontalPadding * 2);
    }
}
