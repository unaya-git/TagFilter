using System;
using System.IO;
using System.Xml.Serialization;

namespace TagFilter
{
    [XmlRoot("LoraTrainingSettings")]
    public class LoraTrainingSettings
    {
        // ── Paths ────────────────────────────────────────────────────────
        public string KohyaPath      { get; set; } = "";   // kohya_ss folder
        public string BaseModelPath  { get; set; } = "";   // .safetensors
        public string OutputDir      { get; set; } = "";
        public string OutputName     { get; set; } = "my_lora";

        public int Repeats { get; set; } = 10;

        // ── Network ──────────────────────────────────────────────────────
        public int    NetworkDim     { get; set; } = 32;
        public int    NetworkAlpha   { get; set; } = 16;

        // ── Training ─────────────────────────────────────────────────────
        public int    MaxTrainEpochs    { get; set; } = 10;
        public int    TrainBatchSize    { get; set; } = 1;
        public int    SaveEveryNEpochs  { get; set; } = 2;
        public int    Resolution        { get; set; } = 1024;
        public bool   EnableBucket      { get; set; } = true;
        //public bool   BucketNoUpscale   { get; set; } = true;
        public bool BucketNoUpscale { get; set; } = false;

        // ── Learning Rate ────────────────────────────────────────────────
        public string LearningRate          { get; set; } = "1e-4";
        public string UnetLr                { get; set; } = "1e-4";
        public string TextEncoderLr         { get; set; } = "5e-5";
        public string LrScheduler          { get; set; } = "cosine_with_restarts";
        public int    LrWarmupSteps         { get; set; } = 0;
        public int    LrSchedulerNumCycles  { get; set; } = 1;

        // ── Optimizer ────────────────────────────────────────────────────
        public string OptimizerType        { get; set; } = "AdamW8bit";
        public string MixedPrecision       { get; set; } = "bf16";
        public string SavePrecision        { get; set; } = "bf16";
        public bool   GradientCheckpointing { get; set; } = true;

        // ── Advanced ─────────────────────────────────────────────────────
        public double NoiseOffset       { get; set; } = 0.0;
        public double MinSnrGamma       { get; set; } = 5.0;
        public bool   ShuffleCaption    { get; set; } = true;
        public int    KeepTokens        { get; set; } = 1;
        public int    ClipSkip          { get; set; } = 1;
        public bool   NoHalfVae         { get; set; } = true;
        public double ScaleWeightNorms  { get; set; } = 1.0;
        public int    NumCpuThreads     { get; set; } = 1;

        // ── Attention / Cache ─────────────────────────────────────────────
        public string AttentionMode { get; set; } = "sdpa";   // sdpa / xformers / none
        public bool CacheLatents { get; set; } = false;    // VRAM節約のためデフォルトOFF
        public bool CacheLatentsToDisk { get; set; } = true;   // ディスクキャッシュはON

        // ── Persistence ──────────────────────────────────────────────────
        private static readonly string SettingsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lora_settings.xml");

        public static LoraTrainingSettings Current { get; private set; }
            = new LoraTrainingSettings();

        public static LoraTrainingSettings Load()
        {
            if (!File.Exists(SettingsPath))
            {
                Current = new LoraTrainingSettings();
                return Current;
            }
            try
            {
                var xs = new XmlSerializer(typeof(LoraTrainingSettings));
                using (var fs = File.OpenRead(SettingsPath))
                {
                    Current = (LoraTrainingSettings)xs.Deserialize(fs);
                    return Current;
                }
            }
            catch
            {
                Current = new LoraTrainingSettings();
                return Current;
            }
        }

        public void Save()
        {
            try
            {
                LoraTrainingSettings.Current = this;
                var xs = new XmlSerializer(typeof(LoraTrainingSettings));
                using (var fs = File.Create(SettingsPath))
                    xs.Serialize(fs, this);
            }
            catch { }
        }

        // ── Presets ───────────────────────────────────────────────────────
        /// <summary>Anime SDXL (Illustrious / Pony)</summary>
        public static LoraTrainingSettings AnimePreset => new LoraTrainingSettings
        {
            Repeats              = 10,
            NetworkDim           = 32,
            NetworkAlpha         = 16,
            MaxTrainEpochs       = 10,
            TrainBatchSize       = 1,
            SaveEveryNEpochs     = 2,
            Resolution           = 1024,
            EnableBucket         = true,
            BucketNoUpscale      = true,
            LearningRate         = "1e-4",
            UnetLr               = "1e-4",
            TextEncoderLr        = "5e-5",
            LrScheduler          = "cosine_with_restarts",
            LrWarmupSteps        = 0,
            LrSchedulerNumCycles = 1,
            OptimizerType        = "AdamW8bit",
            MixedPrecision       = "bf16",
            SavePrecision        = "bf16",
            GradientCheckpointing = true,
            NoiseOffset          = 0.0,
            MinSnrGamma          = 5.0,
            ShuffleCaption       = true,
            KeepTokens           = 1,
            ClipSkip             = 1,
            NoHalfVae            = true,
            ScaleWeightNorms     = 1.0,
            AttentionMode = "sdpa",
            CacheLatents = false,
            CacheLatentsToDisk = true,
        };

        /// <summary>Photo SDXL (real-world photos)</summary>
        public static LoraTrainingSettings PhotoPreset => new LoraTrainingSettings
        {
            Repeats              = 12,
            NetworkDim           = 64,
            NetworkAlpha         = 32,
            MaxTrainEpochs       = 16,
            TrainBatchSize       = 1,
            SaveEveryNEpochs     = 3,
            Resolution           = 1024,
            EnableBucket         = true,
            BucketNoUpscale      = true,
            LearningRate         = "1e-4",
            UnetLr               = "1e-4",
            TextEncoderLr        = "5e-5",
            LrScheduler          = "cosine_with_restarts",
            LrWarmupSteps        = 100,
            LrSchedulerNumCycles = 1,
            OptimizerType        = "AdamW8bit",
            MixedPrecision       = "bf16",
            SavePrecision        = "bf16",
            GradientCheckpointing = true,
            NoiseOffset          = 0.1,
            MinSnrGamma          = 5.0,
            ShuffleCaption       = true,
            KeepTokens           = 1,
            ClipSkip             = 1,
            NoHalfVae            = true,
            ScaleWeightNorms     = 1.0,
            AttentionMode = "sdpa",
            CacheLatents = false,
            CacheLatentsToDisk = true,
        };

        /// <summary>現在のインスタンスに別のプリセット値を上書きコピー（Paths は維持）</summary>
        public void ApplyPreset(LoraTrainingSettings preset)
        {
            NetworkDim            = preset.NetworkDim;
            NetworkAlpha          = preset.NetworkAlpha;
            MaxTrainEpochs        = preset.MaxTrainEpochs;
            TrainBatchSize        = preset.TrainBatchSize;
            SaveEveryNEpochs      = preset.SaveEveryNEpochs;
            Resolution            = preset.Resolution;
            EnableBucket          = preset.EnableBucket;
            BucketNoUpscale       = preset.BucketNoUpscale;
            LearningRate          = preset.LearningRate;
            UnetLr                = preset.UnetLr;
            TextEncoderLr         = preset.TextEncoderLr;
            LrScheduler           = preset.LrScheduler;
            LrWarmupSteps         = preset.LrWarmupSteps;
            LrSchedulerNumCycles  = preset.LrSchedulerNumCycles;
            OptimizerType         = preset.OptimizerType;
            MixedPrecision        = preset.MixedPrecision;
            SavePrecision         = preset.SavePrecision;
            GradientCheckpointing = preset.GradientCheckpointing;
            NoiseOffset           = preset.NoiseOffset;
            MinSnrGamma           = preset.MinSnrGamma;
            ShuffleCaption        = preset.ShuffleCaption;
            KeepTokens            = preset.KeepTokens;
            ClipSkip              = preset.ClipSkip;
            NoHalfVae             = preset.NoHalfVae;
            ScaleWeightNorms      = preset.ScaleWeightNorms;
        }
    }
}
