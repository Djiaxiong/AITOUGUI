using System;
using System.Collections.Generic;
using UnityEngine;

namespace AIToUGUI.Lite
{
    public sealed class AIToUGUILiteParsedBundle
    {
        private readonly HashSet<string> _warningSet = new HashSet<string>(StringComparer.Ordinal);

        public AIToUGUILiteParsedBundle(TextAsset sourceAsset)
        {
            SourceAsset = sourceAsset;
        }

        public TextAsset SourceAsset { get; }
        public LiteCompiledSiteBundle Bundle { get; internal set; }
        public List<string> Warnings { get; } = new List<string>();
        public bool IsValid => Bundle != null && Bundle.site != null && Bundle.pages != null;
        public int PageCount => Bundle != null && Bundle.pages != null ? Bundle.pages.Length : 0;

        internal void AddWarning(string warning)
        {
            if (string.IsNullOrWhiteSpace(warning) || !_warningSet.Add(warning))
            {
                return;
            }

            Warnings.Add(warning);
        }
    }
}
