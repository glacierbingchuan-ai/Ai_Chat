using System;
using System.Linq;
using System.Security.Cryptography;

namespace AI_Chat.Services.ControlPanel
{
    public static class ControlPanelHelpers
    {
        public static string GenerateSecureKey()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return BitConverter.ToString(bytes).Replace("-", "").ToLower();
        }

        public static string GetQueryParameter(string query, string name)
        {
            if (string.IsNullOrEmpty(query)) return null;
            if (query.StartsWith("?")) query = query.Substring(1);
            var param = query.Split('&').Select(p => p.Split('=')).FirstOrDefault(parts => parts.Length == 2 && parts[0].Equals(name, StringComparison.OrdinalIgnoreCase));
            if (param == null) return null;
            try { return Uri.UnescapeDataString(param[1]); } catch { return param[1]; }
        }

        public static string GetContentType(string ext)
        {
            ext = ext.ToLower();
            return ext == ".css" ? "text/css" :
                   ext == ".js" ? "application/javascript" :
                   ext == ".ico" ? "image/x-icon" : "application/octet-stream";
        }
    }
}
