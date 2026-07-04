using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace AnimCancelPatcher
{
    public class CharacterConfig
    {
        public string CharacterId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Element { get; set; } = string.Empty; //Red Blue Green Yellow Purple
        public int MaxComboStages { get; set; }
        public string IconPath { get; set; } = string.Empty;
        public string RelativeAssetDirectory { get; set; } = string.Empty;
        public Dictionary<int, string> ComboAssets { get; set; } = new Dictionary<int, string>();

        public string Description { get; set; } = "";
        public bool IsFavorite { get; set; } = false;
        public string DeveloperMemo { get; set; } = "";

        [JsonIgnore]
        public string AbsoluteIconPath
        {
            get
            {
                if (IconPath == null || IconPath == "")
                {
                    return "";
                }

                string baseD = AppDomain.CurrentDomain.BaseDirectory;
                string full = Path.Combine(baseD, IconPath);
                return full;
            }
        }
    }
}