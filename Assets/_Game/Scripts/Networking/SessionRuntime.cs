using Emberfall.Core.Content;
using UnityEngine;

namespace Emberfall.Networking
{
    public static class SessionRuntime
    {
        private static ISessionService _current;

        public static ISessionService Current => _current ??= new OfflineSessionService(CreateCurrentCompatibility());

        internal static void Install(ISessionService service)
        {
            if (service == null) return;
            if (_current != null && !object.ReferenceEquals(_current, service)) _current.Shutdown();
            _current = service;
        }

        internal static void Uninstall(ISessionService service)
        {
            if (!object.ReferenceEquals(_current, service)) return;
            _current = new OfflineSessionService(CreateCurrentCompatibility());
        }

        public static SessionCompatibility CreateCurrentCompatibility()
        {
            if (!SemanticVersion.TryParse(Application.version, out SemanticVersion clientVersion))
                clientVersion = new SemanticVersion(0, 7, 0);

            if (ContentPackageRuntime.IsInitialized)
            {
                ContentPackageManifest manifest = ContentPackageRuntime.Current.Snapshot.Manifest;
                return new SessionCompatibility(
                    SessionCompatibility.CurrentProtocolVersion,
                    clientVersion,
                    manifest.SchemaVersion,
                    manifest.ContentVersion);
            }

            return new SessionCompatibility(
                SessionCompatibility.CurrentProtocolVersion,
                clientVersion,
                ContentPackageValidator.SupportedSchemaVersion,
                new SemanticVersion(0, 5, 4));
        }
    }
}
