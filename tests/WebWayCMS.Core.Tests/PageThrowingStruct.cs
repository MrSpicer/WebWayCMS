namespace WebWayCMS.Core.Tests;

public struct PageThrowingStruct
{
    public PageThrowingStruct() => throw new InvalidOperationException("Test exception for coverage");
}
