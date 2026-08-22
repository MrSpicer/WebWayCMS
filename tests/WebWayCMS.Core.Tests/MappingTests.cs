using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Article;
using WebWayCMS.Models.CMSRoute;
using WebWayCMS.Models.ContentBlock;
using WebWayCMS.Models.Page;

namespace WebWayCMS.Core.Tests;

[TestFixture]
public class MappingTests
{
    private IMapper _mapper = null!;

    [SetUp]
    public void SetUp() => _mapper = TestSupport.CreateMapper();

    // --- Mapper core behaviour ---

    [Test]
    public void Map_NullSource_Throws()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => _mapper.Map<ContentBlockViewModel>(null!), Throws.ArgumentNullException);
            Assert.That(() => _mapper.Map<ContentBlockDTO, ContentBlockViewModel>(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void Map_UnregisteredPair_Throws()
    {
        Assert.That(() => _mapper.Map<string>(123), Throws.InvalidOperationException);
    }

    [Test]
    public void Map_TwoTypeOverload_UsesDeclaredSourceType()
    {
        var dto = new ContentBlockDTO
        {
            Content = "C",
            Version = new ContentVersion { Node = new ContentNode { Id = Guid.NewGuid() }, Title = "T" }
        };

        var vm = _mapper.Map<ContentBlockDTO, ContentBlockViewModel>(dto);

        Assert.That(vm.Title, Is.EqualTo("T"));
    }

    [Test]
    public void MapperConfiguration_NullConfigure_Throws()
    {
        Assert.That(() => new MapperConfiguration(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void MapperConfiguration_AddNullProfile_Throws()
    {
        Assert.That(() => new MapperConfiguration(c => c.AddProfile(null!)), Throws.ArgumentNullException);
    }

    [Test]
    public void Mapper_NullMaps_Throws()
    {
        Assert.That(() => new MapperConfiguration(_ => { }).CreateMapper(), Throws.Nothing);
    }

    // --- ContentBlock ---

    [Test]
    public void Map_ContentBlockDto_AllTargets_FullAndNull()
    {
        var full = new ContentBlockDTO
        {
            Content = "c",
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow },
                Title = "T",
                Slug = "s",
                PublishStartUtc = DateTime.UtcNow,
                VersionNumber = 3
            }
        };
        var nulls = new ContentBlockDTO
        {
            Content = null!,
            Version = new ContentVersion { Node = new ContentNode { Id = Guid.NewGuid() }, Title = null!, Slug = null! }
        };

        Assert.Multiple(() =>
        {
            Assert.That(_mapper.Map<ContentBlockViewModel>(full).Content, Is.EqualTo("c"));
            Assert.That(_mapper.Map<ContentBlockViewModel>(nulls).Content, Is.Empty);
            Assert.That(_mapper.Map<ContentBlockUpsertViewModel>(full).Title, Is.EqualTo("T"));
            Assert.That(_mapper.Map<ContentBlockUpsertViewModel>(nulls).Slug, Is.Empty);
            Assert.That(_mapper.Map<ContentBlockItemViewModel>(full).Title, Is.EqualTo("T"));
            Assert.That(_mapper.Map<ContentBlockItemViewModel>(nulls).Slug, Is.Empty);
        });
    }

    [Test]
    public void Map_ContentBlockUpsert_ToDto_NodeIdAndDatesPreserved()
    {
        var id = Guid.NewGuid();
        var vm = new ContentBlockUpsertViewModel
        {
            NodeId = id,
            Title = "T",
            Slug = "slug",
            Content = "c",
            PublicationDate = new DateTime(2024, 1, 1),
            PublicationEndDate = new DateTime(2024, 2, 1)
        };

        var dto = _mapper.Map<ContentBlockDTO>(vm);

        Assert.Multiple(() =>
        {
            Assert.That(dto.Version.Node.Id, Is.EqualTo(id));
            Assert.That(dto.Version.Title, Is.EqualTo("T"));
            Assert.That(dto.Version.Slug, Is.EqualTo("slug"));
            Assert.That(dto.Content, Is.EqualTo("c"));
            Assert.That(dto.Version.PublishStartUtc!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(dto.Version.PublishEndUtc, Is.Not.Null);
        });
    }

    [Test]
    public void Map_ContentBlockUpsert_ToDto_NullDefaults()
    {
        var vm = new ContentBlockUpsertViewModel
        {
            NodeId = null,
            Title = null!,
            Slug = null!,
            Content = null!,
            PublicationDate = null,
            PublicationEndDate = null
        };

        var dto = _mapper.Map<ContentBlockDTO>(vm);

        Assert.Multiple(() =>
        {
            Assert.That(dto.Version.Node.Id, Is.EqualTo(Guid.Empty));
            Assert.That(dto.Version.Title, Is.Empty);
            Assert.That(dto.Version.Slug, Is.Empty);
            Assert.That(dto.Content, Is.Empty);
            Assert.That(dto.Version.PublishStartUtc, Is.Null);
            Assert.That(dto.Version.PublishEndUtc, Is.Null);
        });
    }

    // --- Article ---

    [Test]
    public void Map_ArticleDto_ToViewModels_FullAndDefaults()
    {
        var full = new ArticleDTO
        {
            Body = "b",
            AuthorName = "a",
            Summary = "sum",
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow },
                Title = "T",
                Slug = "s",
                PublishStartUtc = DateTime.UtcNow
            }
        };
        var defaults = new ArticleDTO
        {
            Body = "b",
            AuthorName = "a",
            Summary = "s",
            Version = new ContentVersion { Node = new ContentNode { Id = Guid.NewGuid() }, Title = "T", Slug = null! }
        };

        Assert.Multiple(() =>
        {
            Assert.That(_mapper.Map<ArticleViewModel>(full).Body, Is.EqualTo("b"));
            Assert.That(_mapper.Map<ArticleUpsertViewModel>(full).PublicationDate, Is.Not.Null);
            Assert.That(_mapper.Map<ArticleUpsertViewModel>(defaults).PublicationDate, Is.Null);
            Assert.That(_mapper.Map<ArticleUpsertViewModel>(defaults).Slug, Is.Empty);
        });
    }

    [Test]
    public void Map_ArticleUpsert_ToDto_EmptyNodeIdAndDefaults()
    {
        var vm = new ArticleUpsertViewModel
        {
            NodeId = Guid.Empty,
            Title = null!,
            Slug = null!,
            Body = null!,
            AuthorName = null!,
            Summary = null!,
            PublicationDate = new DateTime(2024, 1, 1),
            PublicationEndDate = new DateTime(2024, 2, 1)
        };

        var dto = _mapper.Map<ArticleDTO>(vm);

        Assert.Multiple(() =>
        {
            Assert.That(dto.Version.Node.Id, Is.EqualTo(Guid.Empty));
            Assert.That(dto.Version.PublishEndUtc, Is.Not.Null);
            Assert.That(dto.Version.PublishStartUtc!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(dto.Body, Is.Empty);
            Assert.That(dto.AuthorName, Is.Empty);
            Assert.That(dto.Summary, Is.Empty);
        });
    }

    // --- ArticleList ---

    [Test]
    public void Map_ArticleList_AllDirections()
    {
        var full = new ArticleListDTO
        {
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow },
                Title = "T",
                Slug = "s",
                PublishStartUtc = DateTime.UtcNow
            }
        };
        var defaults = new ArticleListDTO
        {
            Version = new ContentVersion { Node = new ContentNode { Id = Guid.NewGuid() }, Title = "T", Slug = null! }
        };

        Assert.Multiple(() =>
        {
            Assert.That(_mapper.Map<ArticleListUpsertViewModel>(full).PublicationDate, Is.Not.Null);
            Assert.That(_mapper.Map<ArticleListUpsertViewModel>(defaults).PublicationDate, Is.Null);
            Assert.That(_mapper.Map<ArticleListItemViewModel>(full).Title, Is.EqualTo("T"));
            Assert.That(_mapper.Map<ArticleListItemViewModel>(defaults).Slug, Is.Empty);
        });

        var vm = new ArticleListUpsertViewModel { NodeId = null, Title = null!, Slug = null! };
        Assert.That(_mapper.Map<ArticleListDTO>(vm).Version.Node.Id, Is.EqualTo(Guid.Empty));
    }

    // --- Page ---

    [Test]
    public void Map_Page_AllDirections_FullAndNull()
    {
        var full = new PageDTO
        {
            ConfigurationJson = "{}",
            ViewName = "V",
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = Guid.NewGuid(), CreatedUtc = DateTime.UtcNow },
                Title = "T",
                Slug = "s",
                PublishStartUtc = DateTime.UtcNow,
                State = ContentVersionState.Published
            }
        };
        var nulls = new PageDTO
        {
            ConfigurationJson = null!,
            Version = new ContentVersion { Node = new ContentNode { Id = Guid.NewGuid() }, Title = null!, Slug = null! }
        };

        Assert.Multiple(() =>
        {
            Assert.That(_mapper.Map<PageUpsertViewModel>(full).ConfigurationJson, Is.EqualTo("{}"));
            Assert.That(_mapper.Map<PageUpsertViewModel>(full).ViewName, Is.EqualTo("V"));
            Assert.That(_mapper.Map<PageUpsertViewModel>(full).PublicationDate, Is.Not.Null);
            Assert.That(_mapper.Map<PageUpsertViewModel>(nulls).ConfigurationJson, Is.EqualTo("{}"));
            Assert.That(_mapper.Map<PageUpsertViewModel>(nulls).PublicationDate, Is.Null);
            Assert.That(_mapper.Map<PageItemViewModel>(full).Title, Is.EqualTo("T"));
            Assert.That(_mapper.Map<PageItemViewModel>(full).IsPublished, Is.True);
            Assert.That(_mapper.Map<PageItemViewModel>(nulls).Title, Is.Empty);
            Assert.That(_mapper.Map<PageItemViewModel>(nulls).IsPublished, Is.False);
        });
    }

    [Test]
    public void Map_PageUpsert_ToDto_NodeIdIsPreserved()
    {
        var id = Guid.NewGuid();
        var vm = new PageUpsertViewModel
        {
            NodeId = id,
            Title = null!,
            Slug = null!,
            ControllerName = null!,
            ConfigurationJson = null!,
            PublicationDate = new DateTime(2024, 1, 1),
            PublicationEndDate = new DateTime(2024, 2, 1)
        };

        var dto = _mapper.Map<PageDTO>(vm);

        Assert.Multiple(() =>
        {
            Assert.That(dto.Version.Node.Id, Is.EqualTo(id));
            Assert.That(dto.ConfigurationJson, Is.EqualTo("{}"));
            Assert.That(dto.ControllerName, Is.Empty);
            Assert.That(dto.Version.PublishEndUtc, Is.Not.Null);
        });
    }

    [Test]
    public void Map_PageUpsert_ToDto_CarriesParentNodeId()
    {
        var parentNodeId = Guid.NewGuid();
        var vm = new PageUpsertViewModel
        {
            NodeId = Guid.NewGuid(),
            ParentNodeId = parentNodeId,
            Title = "Child",
            Slug = "child",
            ControllerName = "GenericPage"
        };

        var dto = _mapper.Map<PageDTO>(vm);

        Assert.That(dto.Version.Node.ParentNodeId, Is.EqualTo(parentNodeId));
    }

    // --- CMSRoute ---

    [Test]
    public void Map_CMSRouteDto_ToUpsertViewModel_FullAndNull()
    {
        var full = new CMSRouteDTO
        {
            Id = Guid.NewGuid(),
            Pattern = "/test",
            DefaultsJson = "{\"a\":1}",
            ConstraintsJson = "{\"b\":2}",
            DataTokensJson = "{\"c\":3}",
            OwningContentType = "Page",
            NavigationName = "About Us",
            IsReserved = true
        };
        var nulls = new CMSRouteDTO
        {
            Pattern = null!,
            DefaultsJson = null!,
            ConstraintsJson = null!,
            DataTokensJson = null!
        };

        Assert.Multiple(() =>
        {
            var vm = _mapper.Map<CMSRouteUpsertViewModel>(full);
            Assert.That(vm.IsReserved, Is.True);
            Assert.That(vm.Pattern, Is.EqualTo("/test"));
            Assert.That(vm.NavigationName, Is.EqualTo("About Us"));
            Assert.That(vm.DefaultsJson, Is.EqualTo("{\"a\":1}"));

            var n = _mapper.Map<CMSRouteUpsertViewModel>(nulls);
            Assert.That(n.Pattern, Is.Empty);
            Assert.That(n.NavigationName, Is.Null);
            Assert.That(n.DefaultsJson, Is.EqualTo("{}"));
            Assert.That(n.ConstraintsJson, Is.EqualTo("{}"));
            Assert.That(n.DataTokensJson, Is.EqualTo("{}"));
        });
    }

    [Test]
    public void Map_CMSRouteUpsert_ToDto_FullAndNull()
    {
        var full = new CMSRouteUpsertViewModel
        {
            Id = Guid.NewGuid(),
            Pattern = "/test",
            DefaultsJson = "{\"a\":1}",
            ConstraintsJson = "{\"b\":2}",
            DataTokensJson = "{\"c\":3}",
            IsReserved = true,
            OwningContentType = "Page",
            NavigationName = "About Us"
        };

        Assert.Multiple(() =>
        {
            var dto = _mapper.Map<CMSRouteDTO>(full);
            Assert.That(dto.Id, Is.EqualTo(full.Id));
            Assert.That(dto.IsReserved, Is.True);
            Assert.That(dto.Pattern, Is.EqualTo("/test"));
            Assert.That(dto.NavigationName, Is.EqualTo("About Us"));

            var n = _mapper.Map<CMSRouteDTO>(new CMSRouteUpsertViewModel
            {
                Id = null,
                Pattern = null!,
                DefaultsJson = null!,
                ConstraintsJson = null!,
                DataTokensJson = null!
            });
            Assert.That(n.Id, Is.EqualTo(Guid.Empty));
            Assert.That(n.Pattern, Is.Empty);
            Assert.That(n.NavigationName, Is.Null);
            Assert.That(n.DefaultsJson, Is.EqualTo("{}"));
            Assert.That(n.ConstraintsJson, Is.EqualTo("{}"));
            Assert.That(n.DataTokensJson, Is.EqualTo("{}"));
        });
    }

    // --- DTO -> UpsertViewModel decodes the stored (escaped) slug (#7) ---

    [Test]
    public void PageDTO_ToUpsertViewModel_DecodesEscapedSlug()
    {
        var dto = new PageDTO
        {
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = Guid.NewGuid() },
                Title = "Hello World",
                Slug = Uri.EscapeDataString("hello world")
            }
        };

        var vm = _mapper.Map<PageUpsertViewModel>(dto);

        Assert.That(vm.Slug, Is.EqualTo("hello world"));
    }

    [Test]
    public void ArticleDTO_ToUpsertViewModel_DecodesEscapedSlug()
    {
        var dto = new ArticleDTO
        {
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = Guid.NewGuid() },
                Title = "Hello World",
                Slug = Uri.EscapeDataString("hello world")
            }
        };

        var vm = _mapper.Map<ArticleUpsertViewModel>(dto);

        Assert.That(vm.Slug, Is.EqualTo("hello world"));
    }

    [Test]
    public void ContentBlockDTO_ToUpsertViewModel_DecodesEscapedSlug()
    {
        var dto = new ContentBlockDTO
        {
            Version = new ContentVersion
            {
                Node = new ContentNode { Id = Guid.NewGuid() },
                Title = "Hello World",
                Slug = Uri.EscapeDataString("hello world")
            }
        };

        var vm = _mapper.Map<ContentBlockUpsertViewModel>(dto);

        Assert.That(vm.Slug, Is.EqualTo("hello world"));
    }
}
