using NSwag;

namespace Recommand.Generator.Normalizers;

internal interface ISpecNormalizer
{
    void Normalize(OpenApiDocument document);
}
