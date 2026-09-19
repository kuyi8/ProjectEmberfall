using System;
using System.Globalization;
using System.Text;
using Emberfall.Core.Content;

namespace Emberfall.Networking
{
    public static class SessionCompatibilityCodec
    {
        public const int MaxPayloadBytes = 192;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static byte[] Encode(SessionCompatibility value)
        {
            string payload = string.Format(
                CultureInfo.InvariantCulture,
                "{0}|{1}|{2}|{3}",
                value.ProtocolVersion,
                value.ClientVersion,
                value.ContentSchemaVersion,
                value.ContentVersion);
            byte[] bytes = StrictUtf8.GetBytes(payload);
            if (bytes.Length > MaxPayloadBytes)
                throw new InvalidOperationException("Session compatibility payload exceeds the protocol limit.");
            return bytes;
        }

        public static bool TryDecode(byte[] bytes, out SessionCompatibility value, out string reason)
        {
            value = default;
            reason = string.Empty;
            if (bytes == null || bytes.Length == 0 || bytes.Length > MaxPayloadBytes)
            {
                reason = "握手数据缺失或过长。";
                return false;
            }

            string payload;
            try
            {
                payload = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                reason = "握手数据不是有效 UTF-8。";
                return false;
            }

            string[] parts = payload.Split('|');
            if (parts.Length != 4 ||
                !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int protocol) ||
                !SemanticVersion.TryParse(parts[1], out SemanticVersion clientVersion) ||
                !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int schema) ||
                !SemanticVersion.TryParse(parts[3], out SemanticVersion contentVersion) ||
                protocol <= 0 || schema <= 0)
            {
                reason = "握手数据格式无效。";
                return false;
            }

            value = new SessionCompatibility(protocol, clientVersion, schema, contentVersion);
            return true;
        }

        public static bool IsCompatible(
            SessionCompatibility expected,
            SessionCompatibility candidate,
            out string reason)
        {
            if (candidate.ProtocolVersion != expected.ProtocolVersion)
            {
                reason = $"网络协议不一致（主机 {expected.ProtocolVersion}，客户端 {candidate.ProtocolVersion}）。";
                return false;
            }

            if (candidate.ClientVersion != expected.ClientVersion)
            {
                reason = $"客户端版本不一致（主机 {expected.ClientVersion}，客户端 {candidate.ClientVersion}）。";
                return false;
            }

            if (candidate.ContentSchemaVersion != expected.ContentSchemaVersion)
            {
                reason = $"内容结构版本不一致（主机 {expected.ContentSchemaVersion}，客户端 {candidate.ContentSchemaVersion}）。";
                return false;
            }

            if (candidate.ContentVersion != expected.ContentVersion)
            {
                reason = $"内容版本不一致（主机 {expected.ContentVersion}，客户端 {candidate.ContentVersion}）。";
                return false;
            }

            reason = string.Empty;
            return true;
        }
    }
}
