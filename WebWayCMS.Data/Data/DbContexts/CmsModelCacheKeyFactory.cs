using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace WebWayCMS.Data.DbContexts;

/// <summary>
/// EF Core's default model cache key is <c>(contextType, designTime)</c> and ignores injected
/// services, so two <see cref="CmsDbContext"/> instances carrying different
/// <see cref="ICmsModelExtension"/> sets in the same process would share a stale model. This
/// factory folds the extension instances' types into the key so distinct extension sets produce
/// distinct cached models.
/// </summary>
public sealed class CmsModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime)
    {
        var extensions = context is CmsDbContext cms
            ? cms.ModelExtensionTypes
            : Array.Empty<Type>();

        return new CmsModelCacheKey(context.GetType(), designTime, extensions);
    }

    private sealed class CmsModelCacheKey : IEquatable<CmsModelCacheKey>
    {
        private readonly Type _contextType;
        private readonly bool _designTime;
        private readonly IReadOnlyList<Type> _extensions;

        public CmsModelCacheKey(Type contextType, bool designTime, IReadOnlyList<Type> extensions)
        {
            _contextType = contextType;
            _designTime = designTime;
            _extensions = extensions;
        }

        public bool Equals(CmsModelCacheKey? other)
            => other != null
               && _contextType == other._contextType
               && _designTime == other._designTime
               && _extensions.SequenceEqual(other._extensions);

        public override bool Equals(object? obj) => Equals(obj as CmsModelCacheKey);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(_contextType);
            hash.Add(_designTime);
            foreach (var extension in _extensions)
                hash.Add(extension);
            return hash.ToHashCode();
        }
    }
}
