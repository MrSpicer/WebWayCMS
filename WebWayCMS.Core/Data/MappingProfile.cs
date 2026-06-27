using WebWayCMS.Data.Models;
using WebWayCMS.Mapping;
using WebWayCMS.Models;
using WebWayCMS.Models.Article;
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
            Id = s.ContentMeta.Id,
            MasterId = s.ContentMeta.MasterId,
            Version = s.ContentMeta.Version,
            Title = s.ContentMeta.Title,
            Slug = s.ContentMeta.Slug ?? string.Empty,
            Content = s.Content ?? string.Empty,
            PublicationDate = s.ContentMeta.PublicationDate,
            PublicationEndDate = s.ContentMeta.PublicationEndDate,
            IsPublished = s.ContentMeta.IsPublished,
            IsArchived = s.ContentMeta.IsArchived,
            IsHidden = s.ContentMeta.IsHidden,
            IsDeleted = s.ContentMeta.IsDeleted,
            CreationDate = s.ContentMeta.CreationDate,
            ModificationDate = s.ContentMeta.ModificationDate
        });

        CreateMap<ContentBlockDTO, ContentBlockUpsertViewModel>(s => new ContentBlockUpsertViewModel
        {
            Id = s.ContentMeta.Id,
            MasterId = s.ContentMeta.MasterId,
            Version = s.ContentMeta.Version,
            Title = s.ContentMeta.Title ?? string.Empty,
            Slug = s.ContentMeta.Slug ?? string.Empty,
            Content = s.Content ?? string.Empty,
            PublicationDate = s.ContentMeta.PublicationDate,
            PublicationEndDate = s.ContentMeta.PublicationEndDate,
            IsPublished = s.ContentMeta.IsPublished,
            IsArchived = s.ContentMeta.IsArchived,
            IsHidden = s.ContentMeta.IsHidden,
            IsDeleted = s.ContentMeta.IsDeleted,
            CreationDate = s.ContentMeta.CreationDate,
            ModificationDate = s.ContentMeta.ModificationDate
        });

        CreateMap<ContentBlockUpsertViewModel, ContentBlockDTO>(s =>
        {
            var meta = NewContentMeta(s);
            return new ContentBlockDTO
            {
                ContentId = meta.Id,
                Content = s.Content ?? string.Empty,
                ContentMeta = meta
            };
        });

        CreateMap<ContentBlockDTO, ContentBlockItemViewModel>(s => new ContentBlockItemViewModel
        {
            Id = s.ContentMeta.Id,
            MasterId = s.ContentMeta.MasterId,
            Version = s.ContentMeta.Version,
            Title = s.ContentMeta.Title ?? string.Empty,
            Slug = s.ContentMeta.Slug ?? string.Empty,
            CreationDate = s.ContentMeta.CreationDate,
            ModificationDate = s.ContentMeta.ModificationDate
        });

        // Article mappings
        CreateMap<ArticleDTO, ArticleViewModel>(s => new ArticleViewModel
        {
            Id = s.ContentMeta.Id,
            MasterId = s.ContentMeta.MasterId,
            Version = s.ContentMeta.Version,
            Title = s.ContentMeta.Title,
            Slug = s.ContentMeta.Slug,
            Body = s.Body,
            AuthorName = s.AuthorName,
            ArticleListId = s.ArticleListMasterId,
            PublicationDate = s.ContentMeta.PublicationDate,
            PublicationEndDate = s.ContentMeta.PublicationEndDate,
            IsPublished = s.ContentMeta.IsPublished,
            IsArchived = s.ContentMeta.IsArchived,
            IsHidden = s.ContentMeta.IsHidden,
            IsDeleted = s.ContentMeta.IsDeleted,
            CreationDate = s.ContentMeta.CreationDate,
            ModificationDate = s.ContentMeta.ModificationDate
        });

        CreateMap<ArticleDTO, ArticleUpsertViewModel>(s => new ArticleUpsertViewModel
        {
            Id = s.ContentMeta.Id,
            MasterId = s.ContentMeta.MasterId,
            Version = s.ContentMeta.Version,
            Title = s.ContentMeta.Title,
            Slug = s.ContentMeta.Slug ?? string.Empty,
            Body = s.Body,
            Summary = s.Summary,
            AuthorName = s.AuthorName,
            ArticleListId = s.ArticleListMasterId,
            PublicationDate = s.ContentMeta.PublicationDate == default ? (DateTime?)null : s.ContentMeta.PublicationDate,
            PublicationEndDate = s.ContentMeta.PublicationEndDate,
            IsPublished = s.ContentMeta.IsPublished,
            IsArchived = s.ContentMeta.IsArchived,
            IsHidden = s.ContentMeta.IsHidden,
            IsDeleted = s.ContentMeta.IsDeleted,
            CreationDate = s.ContentMeta.CreationDate,
            ModificationDate = s.ContentMeta.ModificationDate
        });

        CreateMap<ArticleUpsertViewModel, ArticleDTO>(s =>
        {
            var meta = NewContentMeta(s);
            return new ArticleDTO
            {
                ContentId = meta.Id,
                Body = s.Body ?? string.Empty,
                AuthorName = s.AuthorName ?? string.Empty,
                Summary = s.Summary ?? string.Empty,
                ArticleListMasterId = s.ArticleListId,
                ContentMeta = meta
            };
        });

        // ArticleList mappings
        CreateMap<ArticleListDTO, ArticleListUpsertViewModel>(s => new ArticleListUpsertViewModel
        {
            Id = s.ContentMeta.Id,
            MasterId = s.ContentMeta.MasterId,
            Version = s.ContentMeta.Version,
            Title = s.ContentMeta.Title,
            Slug = s.ContentMeta.Slug ?? string.Empty,
            PublicationDate = s.ContentMeta.PublicationDate == default ? (DateTime?)null : s.ContentMeta.PublicationDate,
            PublicationEndDate = s.ContentMeta.PublicationEndDate,
            IsPublished = s.ContentMeta.IsPublished,
            IsArchived = s.ContentMeta.IsArchived,
            IsHidden = s.ContentMeta.IsHidden,
            IsDeleted = s.ContentMeta.IsDeleted,
            CreationDate = s.ContentMeta.CreationDate,
            ModificationDate = s.ContentMeta.ModificationDate
        });

        CreateMap<ArticleListUpsertViewModel, ArticleListDTO>(s =>
        {
            var meta = NewContentMeta(s);
            return new ArticleListDTO
            {
                ContentId = meta.Id,
                ContentMeta = meta
            };
        });

        CreateMap<ArticleListDTO, ArticleListItemViewModel>(s => new ArticleListItemViewModel
        {
            Id = s.ContentMeta.Id,
            MasterId = s.ContentMeta.MasterId,
            Version = s.ContentMeta.Version,
            Title = s.ContentMeta.Title ?? string.Empty,
            Slug = s.ContentMeta.Slug ?? string.Empty,
            CreationDate = s.ContentMeta.CreationDate,
            ModificationDate = s.ContentMeta.ModificationDate
        });

        // Page mappings
        CreateMap<PageDTO, PageUpsertViewModel>(s => new PageUpsertViewModel
        {
            Id = s.ContentMeta.Id,
            MasterId = s.ContentMeta.MasterId,
            Version = s.ContentMeta.Version,
            Title = s.ContentMeta.Title ?? string.Empty,
            Slug = s.ContentMeta.Slug ?? string.Empty,
            Route = s.Route ?? string.Empty,
            ControllerName = s.ControllerName ?? string.Empty,
            ConfigurationJson = s.ConfigurationJson ?? "{}",
            ViewName = s.ViewName,
            PublicationDate = s.ContentMeta.PublicationDate == default ? (DateTime?)null : s.ContentMeta.PublicationDate,
            PublicationEndDate = s.ContentMeta.PublicationEndDate,
            IsPublished = s.ContentMeta.IsPublished,
            IsArchived = s.ContentMeta.IsArchived,
            IsHidden = s.ContentMeta.IsHidden,
            IsDeleted = s.ContentMeta.IsDeleted,
            CreationDate = s.ContentMeta.CreationDate,
            ModificationDate = s.ContentMeta.ModificationDate
        });

        CreateMap<PageUpsertViewModel, PageDTO>(s =>
        {
            var meta = NewContentMeta(s);
            return new PageDTO
            {
                ContentId = meta.Id,
                Route = s.Route ?? string.Empty,
                ControllerName = s.ControllerName ?? string.Empty,
                ConfigurationJson = s.ConfigurationJson ?? "{}",
                ViewName = s.ViewName,
                ContentMeta = meta
            };
        });

        CreateMap<PageDTO, PageItemViewModel>(s => new PageItemViewModel
        {
            Id = s.ContentMeta.Id,
            MasterId = s.ContentMeta.MasterId,
            Version = s.ContentMeta.Version,
            Title = s.ContentMeta.Title ?? string.Empty,
            Route = s.Route ?? string.Empty,
            ControllerName = s.ControllerName ?? string.Empty,
            IsPublished = s.ContentMeta.IsPublished,
            CreationDate = s.ContentMeta.CreationDate,
            ModificationDate = s.ContentMeta.ModificationDate
        });
    }

    private static Guid NewId(BaseContentViewModel s)
        => s.Id is { } id && id != Guid.Empty ? id : Guid.NewGuid();

    /// <summary>
    /// Builds the shared <see cref="ContentDTO"/> for an upsert view model, mirroring the flat
    /// fields the view model carries. The Id matches the content type's ContentId via NewId.
    /// </summary>
    private static ContentDTO NewContentMeta(BaseContentViewModel s)
        => new()
        {
            Id = NewId(s),
            Title = s.Title ?? string.Empty,
            Slug = Uri.EscapeDataString(s.Slug ?? string.Empty),
            CreationDate = DateTime.UtcNow,
            ModificationDate = DateTime.UtcNow,
            PublicationDate = PubDate(s),
            PublicationEndDate = PubEnd(s),
            IsPublished = s.IsPublished,
            IsArchived = s.IsArchived,
            IsHidden = s.IsHidden,
            IsDeleted = s.IsDeleted,
            MasterId = s.MasterId ?? Guid.Empty,
            Version = s.Version ?? 0
        };

    private static DateTime PubDate(BaseContentViewModel s)
        => DateTime.SpecifyKind(s.PublicationDate ?? DateTime.UtcNow, DateTimeKind.Utc);

    private static DateTime? PubEnd(BaseContentViewModel s)
        => s.PublicationEndDate.HasValue
            ? DateTime.SpecifyKind(s.PublicationEndDate.Value, DateTimeKind.Utc)
            : (DateTime?)null;
}