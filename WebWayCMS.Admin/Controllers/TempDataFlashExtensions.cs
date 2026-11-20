using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace WebWayCMS.Controllers.Admin;

/// <summary>Sets the admin flash message (<see cref="FlashKeys"/>) that the next admin page shows once.</summary>
public static class TempDataFlashExtensions
{
    public static void FlashSuccess(this ITempDataDictionary tempData, string? message)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        tempData[FlashKeys.Success] = message;
    }

    public static void FlashError(this ITempDataDictionary tempData, string? message)
    {
        ArgumentNullException.ThrowIfNull(tempData);
        tempData[FlashKeys.Error] = message;
    }

    /// <summary>A success or an error flash, by <paramref name="success"/>.</summary>
    public static void Flash(this ITempDataDictionary tempData, bool success, string? message)
    {
        if (success)
            tempData.FlashSuccess(message);
        else
            tempData.FlashError(message);
    }
}
