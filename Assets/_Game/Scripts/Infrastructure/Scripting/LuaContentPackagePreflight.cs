using Emberfall.Core.Content;
using Emberfall.Infrastructure.Content;

namespace Emberfall.Infrastructure.Scripting
{
    public sealed class LuaContentPackagePreflight : IContentPackagePreflight
    {
        public bool Validate(ContentPackageSnapshot snapshot, out string failureReason)
        {
            using var runtime = new LuaOrchestrationRuntime();
            return runtime.ValidatePackage(snapshot, out failureReason);
        }
    }
}
