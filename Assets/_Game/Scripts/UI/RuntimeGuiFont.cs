using UnityEngine;

namespace Emberfall.UI
{
    /// <summary>Provides a Windows-friendly dynamic font for the temporary IMGUI screens.</summary>
    internal static class RuntimeGuiFont
    {
        private static Font _font;

        public static Font Chinese
        {
            get
            {
                if (_font == null)
                {
                    _font = Font.CreateDynamicFontFromOSFont(
                        new[] { "Microsoft YaHei UI", "Microsoft YaHei", "SimHei", "Arial" },
                        16);
                }

                return _font;
            }
        }
    }
}
