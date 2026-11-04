using WebWayCMS.Attributes;
using WebWayCMS.Data.Models;
using WebWayCMS.Models;

namespace WebWayCMS.Core.Tests;

public sealed class FixtureFaqUpsertViewModel : BaseContentViewModel
{
    [ContentRelation("faqtopics", Kind = ContentRelationKind.Containment, Required = true, Ordered = true)]
    [FormProperty(Label = "Topic", Order = 3)]
    public Guid FaqTopicId { get; init; }

    [ContentRelation("faqs")]
    public List<Guid> RelatedFaqIds { get; init; } = [];

    [FormProperty(Label = "Answer", Order = 4)]
    public string Answer { get; init; } = string.Empty;
}
