using Emberfall.Core.Content;

namespace Emberfall.Infrastructure.Content
{
    public interface IContentPackageSignatureVerifier
    {
        bool Verify(ContentPackageSnapshot snapshot, out string failureReason);
    }

    public interface IContentPackagePreflight
    {
        bool Validate(ContentPackageSnapshot snapshot, out string failureReason);
    }
}
