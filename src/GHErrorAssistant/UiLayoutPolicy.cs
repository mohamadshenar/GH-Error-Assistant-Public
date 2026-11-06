using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GHErrorAssistant
{
    // Presentation only. No document reads, diagnostic decisions or event subscriptions.
    internal static class UiLayoutPolicy
    {
        internal const int DefaultWidth = 440, DefaultHeight = 680;
        internal const int MinimumWidth = 360, MinimumHeight = 540;
        internal const int Inset = 12, Gap = 8, ButtonHeight = 30;
        internal static int BodyWidth(int viewportWidth) => Math.Max(24, viewportWidth - 2 * Inset - 24);

        // Display-only break opportunities, never applied to reports/clipboard or saved messages.
        // Enumerating text elements avoids breaking surrogate pairs and combining characters.
        internal static string WrapTokens(string text) => Regex.Replace(text ?? "", @"\S{28,}", match => {
            var result = new StringBuilder();
            var elements = StringInfo.GetTextElementEnumerator(match.Value);
            int count = 0;
            while (elements.MoveNext())
            {
                if (count > 0 && count % 24 == 0) result.Append('\u200B');
                result.Append(elements.GetTextElement()); count++;
            }
            return result.ToString();
        });
    }
}
