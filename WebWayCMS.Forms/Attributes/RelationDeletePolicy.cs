namespace WebWayCMS.Attributes;

/// <summary>What deleting a reference's target does to a source that still points at it.</summary>
public enum RelationDeletePolicy
{
    /// <summary>The target can't be deleted while a referrer's current draft or published version uses it.</summary>
    Restrict = 0,

    /// <summary>Deleting the target saves a new draft of each referrer without the relation.</summary>
    Detach = 1
}
