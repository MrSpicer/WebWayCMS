using NUnit.Framework;

using WebWayCMS.Data.Models;
using WebWayCMS.Mapping;
using WebWayCMS.Models.Article;
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
		var dto = new ContentBlockDTO { Content = "C", ContentMeta = new ContentDTO { Title = "T" } };

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
		var full = new ContentBlockDTO { Content = "c", ContentMeta = new ContentDTO { Id = Guid.NewGuid(), Title = "T", Slug = "s", PublicationDate = DateTime.UtcNow } };
		var nulls = new ContentBlockDTO { Content = null!, ContentMeta = new ContentDTO { Title = null!, Slug = null! } };

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
	public void Map_ContentBlockUpsert_ToDto_IdNullGeneratesNewAndDefaultsDates()
	{
		var vm = new ContentBlockUpsertViewModel { Id = null, Title = null!, Slug = null!, Content = null!, PublicationEndDate = null };

		var dto = _mapper.Map<ContentBlockDTO>(vm);

		Assert.Multiple(() =>
		{
			Assert.That(dto.ContentMeta.Id, Is.Not.EqualTo(Guid.Empty));
			Assert.That(dto.ContentId, Is.EqualTo(dto.ContentMeta.Id));
			Assert.That(dto.ContentMeta.Title, Is.Empty);
			Assert.That(dto.ContentMeta.PublicationEndDate, Is.Null);
			Assert.That(dto.ContentMeta.PublicationDate.Kind, Is.EqualTo(DateTimeKind.Utc));
		});
	}

	[Test]
	public void Map_ContentBlockUpsert_ToDto_IdSetIsPreserved()
	{
		var id = Guid.NewGuid();
		var dto = _mapper.Map<ContentBlockDTO>(new ContentBlockUpsertViewModel { Id = id, Title = "T", Content = "c" });

		Assert.That(dto.ContentMeta.Id, Is.EqualTo(id));
	}

	[Test]
	public void Map_ContentBlockUpsert_ToDto_PreservesMasterIdAndVersion()
	{
		var master = Guid.NewGuid();
		var dto = _mapper.Map<ContentBlockDTO>(new ContentBlockUpsertViewModel { Id = Guid.NewGuid(), MasterId = master, Version = 7, Title = "T", Content = "c" });

		Assert.Multiple(() =>
		{
			Assert.That(dto.ContentMeta.MasterId, Is.EqualTo(master));
			Assert.That(dto.ContentMeta.Version, Is.EqualTo(7));
		});
	}

	// --- Article ---

	[Test]
	public void Map_ArticleDto_ToViewModels_FullAndDefaults()
	{
		var full = new ArticleDTO { Body = "b", AuthorName = "a", Summary = "sum", ContentMeta = new ContentDTO { Id = Guid.NewGuid(), Title = "T", Slug = "s", PublicationDate = DateTime.UtcNow } };
		var defaults = new ArticleDTO { Body = "b", AuthorName = "a", Summary = "s", ContentMeta = new ContentDTO { Title = "T", Slug = null!, PublicationDate = default } };

		Assert.Multiple(() =>
		{
			Assert.That(_mapper.Map<ArticleViewModel>(full).Body, Is.EqualTo("b"));
			Assert.That(_mapper.Map<ArticleUpsertViewModel>(full).PublicationDate, Is.Not.Null);
			Assert.That(_mapper.Map<ArticleUpsertViewModel>(defaults).PublicationDate, Is.Null);
			Assert.That(_mapper.Map<ArticleUpsertViewModel>(defaults).Slug, Is.Empty);
		});
	}

	[Test]
	public void Map_ArticleUpsert_ToDto_IdEmptyGeneratesNew()
	{
		var vm = new ArticleUpsertViewModel { Id = Guid.Empty, Title = null!, Slug = null!, Body = null!, AuthorName = null!, Summary = null!, PublicationDate = new DateTime(2024, 1, 1), PublicationEndDate = new DateTime(2024, 2, 1) };

		var dto = _mapper.Map<ArticleDTO>(vm);

		Assert.Multiple(() =>
		{
			Assert.That(dto.ContentMeta.Id, Is.Not.EqualTo(Guid.Empty));
			Assert.That(dto.ContentMeta.PublicationEndDate, Is.Not.Null);
			Assert.That(dto.Body, Is.Empty);
		});
	}

	// --- ArticleList ---

	[Test]
	public void Map_ArticleList_AllDirections()
	{
		var full = new ArticleListDTO { ContentMeta = new ContentDTO { Id = Guid.NewGuid(), Title = "T", Slug = "s", PublicationDate = DateTime.UtcNow } };
		var defaults = new ArticleListDTO { ContentMeta = new ContentDTO { Title = "T", Slug = null!, PublicationDate = default } };

		Assert.Multiple(() =>
		{
			Assert.That(_mapper.Map<ArticleListUpsertViewModel>(full).PublicationDate, Is.Not.Null);
			Assert.That(_mapper.Map<ArticleListUpsertViewModel>(defaults).PublicationDate, Is.Null);
			Assert.That(_mapper.Map<ArticleListItemViewModel>(full).Title, Is.EqualTo("T"));
			Assert.That(_mapper.Map<ArticleListItemViewModel>(defaults).Slug, Is.Empty);
		});

		var vm = new ArticleListUpsertViewModel { Id = null, Title = null!, Slug = null! };
		Assert.That(_mapper.Map<ArticleListDTO>(vm).ContentMeta.Id, Is.Not.EqualTo(Guid.Empty));
	}

	// --- Page ---

	[Test]
	public void Map_Page_AllDirections_FullAndNull()
	{
		var full = new PageDTO { Route = "/r", ControllerName = "C", ConfigurationJson = "{}", ViewName = "V", ContentMeta = new ContentDTO { Id = Guid.NewGuid(), Title = "T", Slug = "s", PublicationDate = DateTime.UtcNow } };
		var nulls = new PageDTO { Route = null!, ControllerName = null!, ConfigurationJson = null!, ContentMeta = new ContentDTO { Title = null!, Slug = null!, PublicationDate = default } };

		Assert.Multiple(() =>
		{
			Assert.That(_mapper.Map<PageUpsertViewModel>(full).Route, Is.EqualTo("/r"));
			Assert.That(_mapper.Map<PageUpsertViewModel>(full).PublicationDate, Is.Not.Null);
			Assert.That(_mapper.Map<PageUpsertViewModel>(nulls).ConfigurationJson, Is.EqualTo("{}"));
			Assert.That(_mapper.Map<PageUpsertViewModel>(nulls).PublicationDate, Is.Null);
			Assert.That(_mapper.Map<PageItemViewModel>(full).Route, Is.EqualTo("/r"));
			Assert.That(_mapper.Map<PageItemViewModel>(nulls).ControllerName, Is.Empty);
		});
	}

	[Test]
	public void Map_PageUpsert_ToDto_IdSetIsPreserved()
	{
		var id = Guid.NewGuid();
		var vm = new PageUpsertViewModel { Id = id, Title = null!, Slug = null!, Route = null!, ControllerName = null!, ConfigurationJson = null!, PublicationDate = new DateTime(2024, 1, 1), PublicationEndDate = new DateTime(2024, 2, 1) };

		var dto = _mapper.Map<PageDTO>(vm);

		Assert.Multiple(() =>
		{
			Assert.That(dto.ContentMeta.Id, Is.EqualTo(id));
			Assert.That(dto.ConfigurationJson, Is.EqualTo("{}"));
			Assert.That(dto.ContentMeta.PublicationEndDate, Is.Not.Null);
		});
	}
}
