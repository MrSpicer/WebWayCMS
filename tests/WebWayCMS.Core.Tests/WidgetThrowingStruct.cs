namespace WebWayCMS.Core.Tests;

public struct WidgetThrowingStruct
{
    public WidgetThrowingStruct() => throw new InvalidOperationException("Test exception for coverage");
}
