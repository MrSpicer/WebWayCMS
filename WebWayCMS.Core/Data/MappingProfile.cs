using WebWayCMS.Data.Models;
using WebWayCMS.Mapping;
using WebWayCMS.Models;
using WebWayCMS.Models.Article;
using WebWayCMS.Models.CMSRoute;
using WebWayCMS.Models.ContentBlock;
using WebWayCMS.Models.Page;

namespace WebWayCMS.Data;

public sealed class MappingProfile : Profile
{
    public MappingProfile()
    {
        // ContentBlock mappings
        CreateMap<ContentBlockDTO, ContentBlockViewModel>(s => new ContentBlockViewModel
        {
            NodeId = s.Version.Node.Id,
            ExpectedVersionNumber = s.Version.VersionNumber,
            Title = s.Version.Title ?? string.Empty,
            Slug = s.Version.Slug ?? string.Empty,
            Content = s.Content ?? string.Empty,
            PublicationDate = s.Version.PublishStartUtc,
            PublicationEndDate = s.Version.PublishEndUtc,
            IsHidden = s.Version.Node.IsHidden,
            CreationDate = s.Version.Node.CreatedUtc,
            ModificationDate = s.Version.CreatedUtc
        });

        CreateMap<ContentBlockDTO, ContentBlockUpsertViewModel>(s => new ContentBlockUpsertViewModel
        {
            NodeId = s.Version.Node.Id,
            ExpectedVersionNumber = s.Version.VersionNumber,
            Title = s.Version.Title ?? string.Empty,
            Slug = System.Net.WebUtility.UrlDecode(s.Version.Slug) ?? string.Empty,
            Content = s.Content ?? string.Empty,
            PublicationDate = s.Version.PublishStartUtc,
            PublicationEndDate = s.Version.PublishEndUtc,
            IsHidden = s.Version.Node.IsHidden,
            IsPublished = s.Version.State == ContentVersionState.Published,
            CreationDate = s.Version.Node.CreatedUtc,
            ModificationDate = s.Version.CreatedUtc
        });

        CreateMap<ContentBlockUpsertViewModel, ContentBlockDTO>(s => new ContentBlockDTO
        {
            Content = s.Content ?? string.Empty,
            Version = NewVersion(s)
        });

        CreateMap<ContentBlockDTO, ContentBlockItemViewModel>(s => new ContentBlockItemViewModel
        {
            NodeId = s.Version.Node.Id,
            Title = s.Version.Title ?? string.Empty,
            Slug = s.Version.Slug ?? string.Empty,
            IsPublished = s.Version.State == ContentVersionState.Published,
            CreationDate = s.Version.Node.CreatedUtc,
            ModificationDate = s.Version.CreatedUtc
        });

        // Article mappings
        CreateMap<ArticleDTO, ArticleViewModel>(s => new ArticleViewModel
        {
            NodeId = s.Version.Node.Id,
            ExpectedVersionNumber = s.Version.VersionNumber,
            Title = s.Version.Title ?? string.Empty,
            Slug = s.Version.Slug ?? string.Empty,
            Body = s.Body ?? string.Empty,
            AuthorName = s.AuthorName ?? string.Empty,
            ArticleListId = s.ArticleListNodeId,
            PublicationDate = s.Version.PublishStartUtc,
            PublicationEndDate = s.Version.PublishEndUtc,
            IsHidden = s.Version.Node.IsHidden,
            CreationDate = s.Version.Node.CreatedUtc,
            ModificationDate = s.Version.CreatedUtc
        });

        CreateMap<ArticleDTO, ArticleUpsertViewModel>(s => new ArticleUpsertViewModel
        {
            NodeId = s.Version.Node.Id,
            ExpectedVersionNumber = s.Version.VersionNumber,
            Title = s.Version.Title ?? string.Empty,
            Slug = System.Net.WebUtility.UrlDecode(s.Version.Slug) ?? string.Empty,
            Body = s.Body ?? string.Empty,
            Summary = s.Summary ?? string.Empty,
            AuthorName = s.AuthorName ?? string.Empty,
            ArticleListId = s.ArticleListNodeId,
            PublicationDate = s.Version.PublishStartUtc,
            PublicationEndDate = s.Version.PublishEndUtc,
            IsHidden = s.Version.Node.IsHidden,
            CreationDate = s.Version.Node.CreatedUtc,
            ModificationDate = s.Version.CreatedUtc
        });

        CreateMap<ArticleUpsertViewModel, ArticleDTO>(s => new ArticleDTO
        {
            Body = s.Body ?? string.Empty,
            AuthorName = s.AuthorName ?? string.Empty,
            Summary = s.Summary ?? string.Empty,
            ArticleListNodeId = s.ArticleListId,
            Version = NewVersion(s)
        });

        // ArticleList mappings
        CreateMap<ArticleListDTO, ArticleListUpsertViewModel>(s => new ArticleListUpsertViewModel
        {
            NodeId = s.Version.Node.Id,
            ExpectedVersionNumber = s.Version.VersionNumber,
            Title = s.Version.Title ?? string.Empty,
            Slug = s.Version.Slug ?? string.Empty,
            PublicationDate = s.Version.PublishStartUtc,
            PublicationEndDate = s.Version.PublishEndUtc,
            IsHidden = s.Version.Node.IsHidden,
            IsPublished = s.Version.State == ContentVersionState.Published,
            CreationDate = s.Version.Node.CreatedUtc,
            ModificationDate = s.Version.CreatedUtc
        });

        CreateMap<ArticleListUpsertViewModel, ArticleListDTO>(s => new ArticleListDTO
        {
            Version = NewVersion(s)
        });

        CreateMap<ArticleListDTO, ArticleListItemViewModel>(s => new ArticleListItemViewModel
        {
            NodeId = s.Version.Node.Id,
            Title = s.Version.Title ?? string.Empty,
            Slug = s.Version.Slug ?? string.Empty,
            IsPublished = s.Version.State == ContentVersionState.Published,
            CreationDate = s.Version.Node.CreatedUtc,
            ModificationDate = s.Version.CreatedUtc
        });

        // Page mappings
        CreateMap<PageDTO, PageUpsertViewModel>(s => new PageUpsertViewModel
        {
            NodeId = s.Version.Node.Id,
            ExpectedVersionNumber = s.Version.VersionNumber,
            Title = s.Version.Title ?? string.Empty,
            Slug = System.Net.WebUtility.UrlDecode(s.Version.Slug) ?? string.Empty,
            ConfigurationJson = s.ConfigurationJson ?? "{}",
            ViewName = s.ViewName,
            ControllerName = s.ControllerName,
            PublicationDate = s.Version.PublishStartUtc,
            PublicationEndDate = s.Version.PublishEndUtc,
            IsHidden = s.Version.Node.IsHidden,
            IsPublished = s.Version.State == ContentVersionState.Published,
            CreationDate = s.Version.Node.CreatedUtc,
            ModificationDate = s.Version.CreatedUtc
        });

        CreateMap<PageUpsertViewModel, PageDTO>(s =>
        {
            var version = NewVersion(s);
            version.Node.ParentNodeId = s.ParentNodeId;
            return new PageDTO
            {
                ConfigurationJson = s.ConfigurationJson ?? "{}",
                ViewName = s.ViewName,
                ControllerName = s.ControllerName ?? string.Empty,
                Version = version
            };
        });

        CreateMap<PageDTO, PageItemViewModel>(s => new PageItemViewModel
        {
            NodeId = s.Version.Node.Id,
            Title = s.Version.Title ?? string.Empty,
            IsPublished = s.Version.State == ContentVersionState.Published,
            CreationDate = s.Version.Node.CreatedUtc,
            ModificationDate = s.Version.CreatedUtc
        });

        // CMSRoute mappings (routes are not versioned)
        CreateMap<CMSRouteDTO, CMSRouteUpsertViewModel>(s => new CMSRouteUpsertViewModel
        {
            Id = s.Id,
            OwningContentNodeId = s.OwningContentNodeId,
            Pattern = s.Pattern ?? string.Empty,
            NavigationName = s.NavigationName,
            DefaultsJson = s.DefaultsJson ?? "{}",
            ConstraintsJson = s.ConstraintsJson ?? "{}",
            DataTokensJson = s.DataTokensJson ?? "{}",
            Order = s.Order,
            OwningContentType = s.OwningContentType,
            IsReserved = s.IsReserved
        });

        CreateMap<CMSRouteUpsertViewModel, CMSRouteDTO>(s => new CMSRouteDTO
        {
            Id = s.Id ?? Guid.Empty,
            OwningContentNodeId = s.OwningContentNodeId,
            Pattern = s.Pattern ?? string.Empty,
            NavigationName = s.NavigationName,
            DefaultsJson = s.DefaultsJson ?? "{}",
            ConstraintsJson = s.ConstraintsJson ?? "{}",
            DataTokensJson = s.DataTokensJson ?? "{}",
            Order = s.Order,
            OwningContentType = s.OwningContentType,
            IsReserved = s.IsReserved
        });
    }

    private static ContentVersion NewVersion(BaseContentViewModel s)
        => new()
        {
            Node = new ContentNode
            {
                Id = s.NodeId ?? Guid.Empty,
                IsHidden = s.IsHidden
            },
            Title = s.Title ?? string.Empty,
            Slug = s.Slug ?? string.Empty,
            PublishStartUtc = PubDate(s),
            PublishEndUtc = PubEnd(s)
        };

    private static DateTime? PubDate(BaseContentViewModel s)
        => s.PublicationDate.HasValue
            ? DateTime.SpecifyKind(s.PublicationDate.Value, DateTimeKind.Utc)
            : null;

    private static DateTime? PubEnd(BaseContentViewModel s)
        => s.PublicationEndDate.HasValue
            ? DateTime.SpecifyKind(s.PublicationEndDate.Value, DateTimeKind.Utc)
            : null;
}
