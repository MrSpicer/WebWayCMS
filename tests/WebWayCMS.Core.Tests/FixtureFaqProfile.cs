using WebWayCMS.Data.Models;
using WebWayCMS.Mapping;

namespace WebWayCMS.Core.Tests;

public sealed class FixtureFaqProfile : Profile
{
    public FixtureFaqProfile()
    {
        // No relation code: the mapper's relation step moves FaqTopicId / RelatedFaqIds.
        CreateMap<FixtureFaqUpsertViewModel, FixtureFaqDTO>(s => new FixtureFaqDTO
        {
            Answer = s.Answer,
            Version = new ContentVersion { Title = s.Title, Node = new ContentNode { Id = s.NodeId ?? Guid.Empty } }
        });
        CreateMap<FixtureFaqDTO, FixtureFaqUpsertViewModel>(s => new FixtureFaqUpsertViewModel
        {
            NodeId = s.Version.NodeId,
            ExpectedVersionNumber = s.Version.VersionNumber,
            Title = s.Version.Title,
            Answer = s.Answer
        });
    }
}
