using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace TagFilter
{
    [XmlRoot("AppSettings")]
    public class AppSettings
    {
        public string Language { get; set; } = "Japanese";
        public int ModelIndex { get; set; } = 0;
        public int DeviceIndex { get; set; } = 0;
        public int ParallelCount { get; set; } = 1;
        public int LoraModeIndex { get; set; } = 0;
        public double Threshold { get; set; } = 0.35;
        public bool UseUnderscores { get; set; } = true;  // デフォルト: _ あり（安全側）

        [XmlArray("UnwantedTags")]
        [XmlArrayItem("Tag")]
        public List<string> UnwantedTags { get; set; }
            = new List<string> { "solo", "blue_skin", "white_skin", "1girl" };

        private static readonly string SettingsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.xml");

        // 他クラスから参照できるよう静的プロパティを追加
        public static AppSettings Current { get; private set; } = new AppSettings();

        public static AppSettings Load()
        {
            if (!File.Exists(SettingsPath))
            {
                Current = new AppSettings();  
                return Current;
            }
            try
            {
                var xs = new XmlSerializer(typeof(AppSettings));
                using (var fs = File.OpenRead(SettingsPath))
                {
                    Current = (AppSettings)xs.Deserialize(fs); 
                    return Current;
                }
            }
            catch
            {
                Current = new AppSettings();  
                return Current;
            }
        }

        public void Save()
        {
            try
            {
                var xs = new XmlSerializer(typeof(AppSettings));
                using (var fs = File.Create(SettingsPath))
                    xs.Serialize(fs, this);
            }
            catch { }
        }
    }
}